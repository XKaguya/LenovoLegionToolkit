using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using NeoSmart.AsyncLock;
using Newtonsoft.Json;
using Octokit;
using Octokit.Internal;
using System.Security.Cryptography.X509Certificates;
using LenovoLegionToolkit.Lib.Resources;

namespace LenovoLegionToolkit.Lib.Utils;

public class UpdateChecker
{
    private readonly HttpClientFactory _httpClientFactory;
    private readonly UpdateSettings _updateSettings = IoCContainer.Resolve<UpdateSettings>();
    private readonly AsyncLock _updateSemaphore = new();

    private static readonly Dictionary<string, ProjectEntry> ProjectEntries = new();
    private const string SERVER_URL = "http://kaguya.net.cn:9999";
    private const string TRUSTED_SIGNATURE_THUMBPRINT = "5A6C3448B4D2FECBAA7EE1BB592E4A7EEE6FB7A8";
    private const int MAX_RETRY_COUNT = 3;

    private DateTime _lastUpdate;
    private TimeSpan _minimumTimeSpanForRefresh;
    private Update[] _updates = [];
    public UpdateFromServer? UpdateFromServer;

    public bool Disable { get; set; }
    public UpdateCheckStatus Status { get; set; }

    public UpdateChecker(HttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;

        UpdateMinimumTimeSpanForRefresh();
        _lastUpdate = _updateSettings.Store.LastUpdateCheckDateTime ?? DateTime.MinValue;
    }

    public async Task<Version?> CheckAsync(bool forceCheck)
    {
        using (await _updateSemaphore.LockAsync().ConfigureAwait(false))
        {
            if (Disable)
            {
                _lastUpdate = DateTime.UtcNow;
                _updates = [];
                return null;
            }

            var timeSpanSinceLastUpdate = DateTime.UtcNow - _lastUpdate;
            var shouldCheck = timeSpanSinceLastUpdate > _minimumTimeSpanForRefresh;

            if (_updateSettings.Store.UpdateMethod == UpdateMethod.GitHub)
            {
                try
                {
                    if (!forceCheck && !shouldCheck)
                        return _updates.Length != 0 ? _updates.First().Version : null;

                    Log.Instance.Trace($"Checking GitHub for updates...");

                    var adapter = new HttpClientAdapter(_httpClientFactory.CreateHandler);
                    var productInformation = new ProductHeaderValue("LenovoLegionToolkit-UpdateChecker");
                    var connection = new Connection(productInformation, adapter);
                    var githubClient = new GitHubClient(connection);
                    var releases = await githubClient.Repository.Release.GetAll("LenovoLegionToolkit-Team", "LenovoLegionToolkit", new ApiOptions { PageSize = 5 }).ConfigureAwait(false);

                    var thisReleaseVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0);
                    var thisBuildDate = Assembly.GetEntryAssembly()?.GetBuildDateTime() ?? new DateTime(2000, 1, 1);
                    var updateChannel = _updateSettings.Store.UpdateChannel;

                    Log.Instance.Trace($"Found {releases.Count} releases. Current: {thisReleaseVersion} built on {thisBuildDate:yyyy-MM-dd}. Channel: {updateChannel}");
                    foreach (var r in releases)
                    {
                        Log.Instance.Trace($"- {r.TagName} (Draft: {r.Draft}, Pre: {r.Prerelease}, Branch: {r.TargetCommitish}, Date: {r.CreatedAt:yyyy-MM-dd})");
                    }

                    var updates = releases
                        .Where(r => !r.Draft)
                        .Where(r => IsMatchingChannel(r, updateChannel))
                        .Where(r => (r.PublishedAt ?? r.CreatedAt).UtcDateTime >= thisBuildDate)
                        .Select(r => new { Release = r, Version = TryParseReleaseVersion(r.TagName) })
                        .Where(r => r.Version is not null && r.Version > thisReleaseVersion)
                        .OrderByDescending(r => r.Version)
                        .Select(r => new Update(r.Release))
                        .ToArray();

                    Log.Instance.Trace($"Checked [updates.Length={updates.Length}]");

                    _updates = updates;
                    Status = UpdateCheckStatus.Success;

                    return _updates.Length != 0 ? _updates.First().Version : null;
                }
                catch (RateLimitExceededException ex)
                {
                    Log.Instance.Trace($"Reached API Rate Limitation.", ex);

                    Status = UpdateCheckStatus.RateLimitReached;
                    return null;
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Error checking for updates via GitHub.", ex);

                    Status = UpdateCheckStatus.Error;
                    return null;
                }
                finally
                {
                    _lastUpdate = DateTime.UtcNow;
                    _updateSettings.Store.LastUpdateCheckDateTime = _lastUpdate;
                    _updateSettings.SynchronizeStore();
                }
            }
            else
            {
                try
                {
                    if (!forceCheck && !shouldCheck && UpdateFromServer?.Url != null)
                        return Version.Parse(ProjectEntries.Values
                            .Where(entry => entry.ProjectName == $"LenovoLegionToolkit{_updateSettings.Store.UpdateChannel}")
                            .FirstOrDefault().ProjectVersion ?? "0.0.0.0");

                    Log.Instance.Trace($"Checking Server for updates...");
                    var (currentVersion, newVersion, statusCode, projectInfo, patchNote) = await TryGetUpdateFromServer(_updateSettings.Store.UpdateChannel).ConfigureAwait(false);

                    if (statusCode == StatusCode.Null)
                    {
                        Log.Instance.Trace($"Failed to check for updates.");
                        Status = UpdateCheckStatus.Error;
                        return null;
                    }

                    if (currentVersion == newVersion && statusCode != StatusCode.ForceUpdate)
                    {
                        Log.Instance.Trace($"You are already using the latest version.");

                        Status = UpdateCheckStatus.Success;
                        return null;
                    }

                    if (currentVersion > newVersion && statusCode != StatusCode.ForceUpdate)
                    {
                        Log.Instance.Trace($"You are using a private version.");

                        Status = UpdateCheckStatus.Success;
                        return null;
                    }

                    switch (statusCode)
                    {
                        case StatusCode.ForceUpdate when currentVersion != newVersion:
                            Log.Instance.Trace($"Force update branch");

                            Status = UpdateCheckStatus.Success;
                            UpdateFromServer = new UpdateFromServer(projectInfo, patchNote);
                            return newVersion;

                        case StatusCode.Update when currentVersion != newVersion:
                            Log.Instance.Trace($"Normal update branch");

                            Status = UpdateCheckStatus.Success;
                            UpdateFromServer = new UpdateFromServer(projectInfo, patchNote);
                            return newVersion;

                        case StatusCode.NoUpdate:
                        case StatusCode.ForceUpdate when newVersion == currentVersion:
                            Log.Instance.Trace($"No updates are available.");
                            Status = UpdateCheckStatus.Success;
                            return null;

                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
                finally
                {
                    _lastUpdate = DateTime.UtcNow;
                    _updateSettings.Store.LastUpdateCheckDateTime = _lastUpdate;
                    _updateSettings.SynchronizeStore();
                }
            }
        }
    }

    public async Task<Update[]> GetUpdatesAsync()
    {
        using (await _updateSemaphore.LockAsync().ConfigureAwait(false))
            return _updates;
    }

    public async Task<string> DownloadLatestUpdateAsync(IProgress<float>? progress = null, CancellationToken cancellationToken = default)
    {
        using (await _updateSemaphore.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            var tempPath = Path.Combine(Folders.Temp, $"LenovoLegionToolkitSetup_{Guid.NewGuid()}.exe");

            if (_updateSettings.Store.UpdateMethod == UpdateMethod.GitHub)
            {
                var latestUpdate = _updates.OrderByDescending(u => u.Version).FirstOrDefault();

                if (latestUpdate.Url is null)
                    throw new InvalidOperationException("No GitHub updates available");

                await using var fileStream = File.OpenWrite(tempPath);
                using var httpClient = _httpClientFactory.Create();
                await httpClient.DownloadAsync(latestUpdate.Url, fileStream, progress, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (UpdateFromServer == null)
                    throw new InvalidOperationException("No Server updates available");

                if (UpdateFromServer?.Url is null)
                    throw new InvalidOperationException("Setup file URL could not be found");

                using var httpClient = _httpClientFactory.Create();
                using var fileStream = File.Create(tempPath);

                await httpClient.DownloadAsync(UpdateFromServer?.Url!, fileStream, progress, cancellationToken, true).ConfigureAwait(false);
                fileStream.Dispose();

                VerifySignature(tempPath);
            }

            return tempPath;
        }
    }

    public void UpdateMinimumTimeSpanForRefresh() => _minimumTimeSpanForRefresh = _updateSettings.Store.UpdateCheckFrequency switch
    {
        UpdateCheckFrequency.Never => TimeSpan.FromSeconds(0),
        UpdateCheckFrequency.PerHour => TimeSpan.FromHours(1),
        UpdateCheckFrequency.PerThreeHours => TimeSpan.FromHours(3),
        UpdateCheckFrequency.PerTwelveHours => TimeSpan.FromHours(13),
        UpdateCheckFrequency.PerDay => TimeSpan.FromDays(1),
        UpdateCheckFrequency.PerWeek => TimeSpan.FromDays(7),
        UpdateCheckFrequency.PerMonth => TimeSpan.FromDays(30),
        _ => throw new ArgumentException(nameof(_updateSettings.Store.UpdateCheckFrequency))
    };

    private static void VerifySignature(string filePath)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var baseCert = X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
            using var cert = new X509Certificate2(baseCert);

            if (!cert.Thumbprint.Equals(TRUSTED_SIGNATURE_THUMBPRINT, StringComparison.OrdinalIgnoreCase))
            {
                var detail = $"Thumbprint mismatch (Actual: {cert.Thumbprint})";
                throw new SecurityException(string.Format(Resource.UpdateChecker_Security_Thumbprint, detail));
            }
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch { /* Ignore */ }

            Log.Instance.Trace($"Signature verification failed for '{filePath}': {ex.Message}");
            throw new SecurityException(string.Format(Resource.UpdateChecker_Security_Invalid, ex.Message), ex);
        }
    }

    #region GitHub Methods

    private static bool IsMatchingChannel(Release release, UpdateChannel updateChannel)
    {
        switch (updateChannel)
        {
            case UpdateChannel.Stable:
                return IsReleaseOnBranch(release, "master") && !release.Prerelease;
            case UpdateChannel.Beta:
                return IsReleaseOnBranch(release, "master") && release.Prerelease;
            case UpdateChannel.Dev:
                return IsReleaseOnBranch(release, "dev") && !release.Prerelease;
            default:
                return false;
        }
    }

    private static bool IsReleaseOnBranch(Release release, string branch)
    {
        var target = release.TargetCommitish;

        if (string.IsNullOrWhiteSpace(target))
            return branch.Equals("master", StringComparison.OrdinalIgnoreCase);

        return target.Equals(branch, StringComparison.OrdinalIgnoreCase)
               || target.EndsWith($"/{branch}", StringComparison.OrdinalIgnoreCase)
               || target.EndsWith($"\\{branch}", StringComparison.OrdinalIgnoreCase);
    }

    private static Version? TryParseReleaseVersion(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            return null;

        var normalized = tagName.TrimStart('v', 'V');
        if (Version.TryParse(normalized, out var parsed))
            return parsed;

        var numericPart = new string(normalized
            .TakeWhile(c => char.IsDigit(c) || c == '.')
            .ToArray())
            .Trim('.');

        return Version.TryParse(numericPart, out parsed) ? parsed : null;
    }

    #endregion

    #region Server Update Methods

    private static string GetChannelSuffix(UpdateChannel channel) => channel switch
    {
        UpdateChannel.Beta => "Beta",
        UpdateChannel.Dev => "Dev",
        _ => ""
    };

    private static bool IsServerUnderMaintenanceMode()
    {
        return ProjectEntries.ContainsKey("MaintenanceMode") && ProjectEntries["MaintenanceMode"].MaintenanceMode;
    }

    private async Task<(StatusCode, string)> GetLatestVersionWithRetryAsync(ProjectInfo projectInfo, UpdateChannel channel)
    {
        var (status, version) = await RetryAsync(() => GetLatestVersionFromServer(projectInfo, channel)).ConfigureAwait(false);

        Log.Instance.Trace($"Project {projectInfo.ProjectName}");
        Log.Instance.Trace($"Status code: {status.ToString()}");
        Log.Instance.Trace($"Current version is {projectInfo.ProjectCurrentVersion}");
        Log.Instance.Trace($"Latest version is {version}");

        return !string.IsNullOrEmpty(version) ? (status, version) : throw new Exception("Failed to get the latest version.");
    }

    private static async Task<(StatusCode, string)> RetryAsync(Func<Task<(StatusCode, string)>> operation)
    {
        for (int i = 0; i < MAX_RETRY_COUNT; i++)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Attempt {i + 1} failed: {ex.Message}");
                if (i == MAX_RETRY_COUNT - 1) throw;
            }

            await Task.Delay(1000).ConfigureAwait(false);
        }

        return (StatusCode.Null, string.Empty);
    }

    private async Task<(StatusCode, string)> GetLatestVersionFromServer(ProjectInfo projectInfo, UpdateChannel channel)
    {
        try
        {
            using HttpClient httpClient = new HttpClient();

            var url = $"{SERVER_URL}/Projects.json";

            string userAgent = $"CommonUpdater-LenovoLegionToolkit-{(string.IsNullOrEmpty(projectInfo.ProjectCurrentVersion) ? "Null" : projectInfo.ProjectCurrentVersion)}";
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

            HttpResponseMessage response = await httpClient.GetAsync(url).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            string jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            var projectConfig = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonResponse);
            if (projectConfig == null)
            {
                Log.Instance.Trace($"Project configuration is empty or invalid.");
                return (StatusCode.Null, string.Empty);
            }

            ProjectEntries.Clear();

            var maintenanceEntry = new ProjectEntry();

            if (projectConfig.TryGetValue("MaintenanceMode", out var maintenanceObj))
            {
                try
                {
                    maintenanceEntry.MaintenanceMode = Convert.ToBoolean(maintenanceObj);
                }
                catch
                {
                    maintenanceEntry.MaintenanceMode = false;
                }

                ProjectEntries["MaintenanceMode"] = maintenanceEntry;
            }

            foreach (var project in projectConfig)
            {
                if (project.Key == "MaintenanceMode")
                {
                    continue;
                }

                var projectDetails = project.Value?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(projectDetails))
                    continue;

                var details = JsonConvert.DeserializeObject<Dictionary<string, object>>(projectDetails);
                if (details == null)
                    continue;

                if (!details.TryGetValue("Version", out var versionObj))
                    continue;

                string version = versionObj?.ToString() ?? string.Empty;
                bool forceUpdate = false;
                if (details.TryGetValue("ForceUpdate", out var forceObj))
                {
                    try
                    {
                        forceUpdate = Convert.ToBoolean(forceObj);
                    }
                    catch
                    {
                        forceUpdate = false;
                    }
                }

                ProjectEntries[project.Key] = new ProjectEntry
                {
                    ProjectName = project.Key,
                    ProjectCurrentVersion = projectInfo.ProjectCurrentVersion ?? string.Empty,
                    ProjectVersion = version,
                    ProjectForceUpdate = forceUpdate
                };
            }

            foreach (var kvp in ProjectEntries)
            {
                Log.Instance.Trace(kvp.Key == "MaintenanceMode"
                    ? (FormattableString)$"MaintenanceMode: {kvp.Value.MaintenanceMode}"
                    : (FormattableString)
                    $"Project: {kvp.Value.ProjectName}, Version: {kvp.Value.ProjectVersion}, Force Update: {kvp.Value.ProjectForceUpdate}");
            }

            string projectName = $"{projectInfo.ProjectName}{GetChannelSuffix(channel)}";

            if (!ProjectEntries.TryGetValue(projectName, out var entry))
            {
                Log.Instance.Trace($"Project entry '{projectName}' not found in configuration.");
                return (StatusCode.Null, string.Empty);
            }

            if (!entry.IsValid())
            {
                return (StatusCode.Null, string.Empty);
            }

            Version currentVersion = Version.Parse(ProjectEntries[projectName].ProjectCurrentVersion);
            Version projectVersion = Version.Parse(ProjectEntries[projectName].ProjectVersion);

            if (projectVersion != currentVersion && ProjectEntries[projectName].ProjectForceUpdate)
            {
                return (StatusCode.ForceUpdate, ProjectEntries[projectName].ProjectVersion);
            }
            if (projectVersion != currentVersion)
            {
                return (StatusCode.Update, ProjectEntries[projectName].ProjectVersion);
            }

            return projectVersion == currentVersion ? (StatusCode.NoUpdate, ProjectEntries[projectName].ProjectVersion) : (StatusCode.Null, string.Empty);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Error fetching version from server: {ex.Message}");
            return (StatusCode.Null, string.Empty);
        }
    }

    private async Task<(Version?, Version?, StatusCode, ProjectInfo, string)> TryGetUpdateFromServer(UpdateChannel channel)
    {
        var thisReleaseVersion = Assembly.GetEntryAssembly()?.GetName().Version;
        string folderName = $"LenovoLegionToolkit{GetChannelSuffix(channel)}";

        ProjectInfo projectInfo = new ProjectInfo
        {
            ProjectName = "LenovoLegionToolkit",
            ProjectExeName = "LenovoLegionToolkitSetup.exe",
            ProjectAuthor = "LenovoLegionToolkit-Team",
            ProjectCurrentVersion = thisReleaseVersion?.ToString() ?? "0.0.0.0",
            ProjectCurrentExePath = "NULL",
            ProjectNewExePath = $"{SERVER_URL}/{folderName}/LenovoLegionToolkitSetup.exe"
        };

        var (statusCode, newestVersion) = await GetLatestVersionWithRetryAsync(projectInfo, channel).ConfigureAwait(false);

        if (IsServerUnderMaintenanceMode())
        {
            Log.Instance.Trace($"Update Server is currently under maintenance mode.");
            Log.Instance.Trace($"Current channel is {channel}");
            Log.Instance.Trace($"Now exiting...");

            Status = UpdateCheckStatus.Success;
            return (null, null, StatusCode.Null, new ProjectInfo(), "");
        }

        projectInfo.ProjectNewVersion = newestVersion;
        var currentVersion = Version.Parse(projectInfo.ProjectCurrentVersion);
        var newVersion = Version.Parse(newestVersion);
        string patchNote = string.Empty;

        if ((statusCode != StatusCode.Update && statusCode != StatusCode.ForceUpdate) ||
            string.IsNullOrEmpty(newestVersion)) return (currentVersion, newVersion, statusCode, projectInfo, patchNote);

        try
        {
            var langData = "en-US";
            if (File.Exists(Path.Combine(Folders.AppData, "lang")))
            {
                langData = await File.ReadAllTextAsync(Path.Combine(Folders.AppData, "lang")).ConfigureAwait(false);
            }

            var cultureInfo = new CultureInfo(langData);
            var patchNoteUrl = cultureInfo.IetfLanguageTag == "zh-Hans" ? $"{SERVER_URL}/{folderName}/PatchNote-{newestVersion}-zh.txt" : $"{SERVER_URL}/{folderName}/PatchNote-{newestVersion}.txt";

            Log.Instance.Trace($"Fetching patch note from: {patchNoteUrl}");

            using var httpClient = _httpClientFactory.Create();
            string userAgent = $"CommonUpdater-LenovoLegionToolkit-{(string.IsNullOrEmpty(projectInfo.ProjectCurrentVersion) ? "Null" : projectInfo.ProjectCurrentVersion)}";
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

            string patchNoteContent = await httpClient.GetStringAsync(patchNoteUrl).ConfigureAwait(false);

            patchNote = patchNoteContent.Replace("\r\n", "\n").Trim();
            Log.Instance.Trace($"Patch note fetched successfully.");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to fetch patch note or no patch note available: {ex.Message}");
            patchNote = "No patch notes available.";
        }

        return (currentVersion, newVersion, statusCode, projectInfo, patchNote);
    }
    #endregion
}

public class SecurityException : Exception
{
    public SecurityException() { }
    public SecurityException(string message) : base(message) { }
    public SecurityException(string message, Exception innerException) : base(message, innerException) { }
}