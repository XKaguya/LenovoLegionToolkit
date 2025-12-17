using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using NeoSmart.AsyncLock;
using Newtonsoft.Json;
using Octokit;
using Octokit.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Utils;

public class UpdateChecker
{
    private readonly HttpClientFactory _httpClientFactory;
    private readonly UpdateCheckSettings _updateCheckSettings = IoCContainer.Resolve<UpdateCheckSettings>();
    private readonly AsyncLock _updateSemaphore = new();

    private static readonly Dictionary<string, ProjectEntry> ProjectEntries = new();
    private static string _branch = "Release";
    private static readonly string Branch = _branch;
    private const string ServerUrl = "http://kaguya.net.cn:9999";
    private const int MaxRetryCount = 3;

    private DateTime _lastUpdate;
    private TimeSpan _minimumTimeSpanForRefresh;
    private Update[] _updates = [];
    private UpdateFromServer _updateFromServer;

    public bool Disable { get; set; }
    public UpdateCheckStatus Status { get; set; }

    public UpdateChecker(HttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;

        UpdateMinimumTimeSpanForRefresh();
        _lastUpdate = _updateCheckSettings.Store.LastUpdateCheckDateTime ?? DateTime.MinValue;
    }

    public async Task<Version?> CheckAsync(bool forceCheck)
    {
#if DEBUG
        return null;
#endif
        using (await _updateSemaphore.LockAsync().ConfigureAwait(false))
        {
            ApplicationSettings settings = IoCContainer.Resolve<ApplicationSettings>();
            if (settings.Store.UpdateMethod == UpdateMethod.Github)
            {
                if (Disable)
                {
                    _lastUpdate = DateTime.UtcNow;
                    _updates = [];
                    return null;
                }

                try
                {
                    var timeSpanSinceLastUpdate = DateTime.UtcNow - _lastUpdate;
                    var shouldCheck = timeSpanSinceLastUpdate > _minimumTimeSpanForRefresh;

                    if (!forceCheck && !shouldCheck)
                        return _updates.Length != 0 ? _updates.First().Version : null;

                    Log.Instance.Trace($"Checking...");

                    var adapter = new HttpClientAdapter(_httpClientFactory.CreateHandler);
                    var productInformation = new ProductHeaderValue("LenovoLegionToolkit-UpdateChecker");
                    var connection = new Connection(productInformation, adapter);
                    var githubClient = new GitHubClient(connection);
                    var releases = await githubClient.Repository.Release.GetAll("XKaguya", "LenovoLegionToolkit", new ApiOptions { PageSize = 5 }).ConfigureAwait(false);

                    var thisReleaseVersion = Assembly.GetEntryAssembly()?.GetName().Version;
                    var thisBuildDate = Assembly.GetEntryAssembly()?.GetBuildDateTime() ?? new DateTime(2000, 1, 1);

                    Log.Instance.Trace($"Found {releases.Count} releases. Current: {thisReleaseVersion} built on {thisBuildDate:yyyy-MM-dd}");
                    foreach (var r in releases)
                    {
                        Log.Instance.Trace($"- {r.TagName} (Draft: {r.Draft}, Pre: {r.Prerelease}, Date: {r.CreatedAt:yyyy-MM-dd})");
                    }

                    var updates = releases
                        .Where(r => !r.Draft)
                        .Where(r => !r.Prerelease)
                        .Where(r => (r.PublishedAt ?? r.CreatedAt).UtcDateTime >= thisBuildDate)
                        .Select(r => new Update(r))
                        .Where(r => r.Version > thisReleaseVersion)
                        .OrderByDescending(r => r.Version)
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
                    Log.Instance.Trace($"Error checking for updates.", ex);

                    Status = UpdateCheckStatus.Error;
                    return null;
                }
                finally
                {
                    _lastUpdate = DateTime.UtcNow;
                    _updateCheckSettings.Store.LastUpdateCheckDateTime = _lastUpdate;
                    _updateCheckSettings.SynchronizeStore();
                }
            }
            else
            {
                try
                {
                    var (currentVersion, newVersion, statusCode, projectInfo) = await TryGetUpdateFromServer();

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

                    if (statusCode == StatusCode.ForceUpdate && currentVersion != newVersion)
                    {
                        Log.Instance.Trace($"Force update branch");

                        Status = UpdateCheckStatus.Success;
                        _updateFromServer = new UpdateFromServer(projectInfo);
                        return newVersion;
                    }
                    if (statusCode == StatusCode.Update && currentVersion != newVersion)
                    {
                        Log.Instance.Trace($"Normal update branch");

                        Status = UpdateCheckStatus.Success;

                        _updateFromServer = new UpdateFromServer(projectInfo);
                        return newVersion;
                    }
                    if (statusCode == StatusCode.NoUpdate || statusCode == StatusCode.ForceUpdate && newVersion == currentVersion)
                    {
                        Log.Instance.Trace($"No updates are available.");
                        Status = UpdateCheckStatus.Success;
                        return null;
                    }
                }
                finally
                {
                    _lastUpdate = DateTime.UtcNow;
                    _updateCheckSettings.Store.LastUpdateCheckDateTime = _lastUpdate;
                    _updateCheckSettings.SynchronizeStore();
                }
            }

            Status = UpdateCheckStatus.Error;
            return null;
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
            var latestUpdate = _updates.OrderByDescending(u => u.Version).FirstOrDefault();

            if (latestUpdate.Url != null)
            {
                if (latestUpdate.Equals(default))
                    throw new InvalidOperationException("No updates available");

                await using var fileStream = File.OpenWrite(tempPath);
                using var httpClient = _httpClientFactory.Create();
                await httpClient.DownloadAsync(latestUpdate.Url, fileStream, progress, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (_updateFromServer.Equals(default))
                    throw new InvalidOperationException("No updates available");

                if (_updateFromServer.Url is null)
                    throw new InvalidOperationException("Setup file URL could not be found");

                await using var fileStream = File.OpenWrite(tempPath);
                using var httpClient = _httpClientFactory.Create();
                await httpClient.DownloadAsync(_updateFromServer.Url, fileStream, progress, cancellationToken, true).ConfigureAwait(false);
            }

            return tempPath;
        }
    }

    public void UpdateMinimumTimeSpanForRefresh() => _minimumTimeSpanForRefresh = _updateCheckSettings.Store.UpdateCheckFrequency switch
    {
        UpdateCheckFrequency.Never => TimeSpan.FromSeconds(0),
        UpdateCheckFrequency.PerHour => TimeSpan.FromHours(1),
        UpdateCheckFrequency.PerThreeHours => TimeSpan.FromHours(3),
        UpdateCheckFrequency.PerTwelveHours => TimeSpan.FromHours(13),
        UpdateCheckFrequency.PerDay => TimeSpan.FromDays(1),
        UpdateCheckFrequency.PerWeek => TimeSpan.FromDays(7),
        UpdateCheckFrequency.PerMonth => TimeSpan.FromDays(30),
        _ => throw new ArgumentException(nameof(_updateCheckSettings.Store.UpdateCheckFrequency))
    };

    private static bool IsServerUnderMaintenanceMode()
    {
        return ProjectEntries.ContainsKey("MaintenanceMode") && ProjectEntries["MaintenanceMode"].MaintenanceMode;
    }

    private static async Task<(StatusCode, string)> GetLatestVersionWithRetryAsync(ProjectInfo projectInfo)
    {
        var (status, version) = await RetryAsync(() => GetLatestVersionFromServer(projectInfo)).ConfigureAwait(false);

        Log.Instance.Trace($"Project {projectInfo.ProjectName}");
        Log.Instance.Trace($"Status code: {status.ToString()}");
        Log.Instance.Trace($"Current version is {projectInfo.ProjectCurrentVersion}");
        Log.Instance.Trace($"Latest version is {version}");

        return !string.IsNullOrEmpty(version) ? (status, version) : throw new Exception("Failed to get the latest version.");
    }

    private static async Task<(StatusCode, string)> RetryAsync(Func<Task<(StatusCode, string)>> operation)
    {
        for (int i = 0; i < MaxRetryCount; i++)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Attempt {i + 1} failed: {ex.Message}");
                if (i == MaxRetryCount - 1) throw;
            }

            await Task.Delay(1000).ConfigureAwait(false);
        }

        return (StatusCode.Null, string.Empty);
    }

    private static async Task<(StatusCode, string)> GetLatestVersionFromServer(ProjectInfo projectInfo)
    {
        try
        {
            using HttpClient httpClient = new HttpClient();

            var url = $"{ServerUrl}/Projects.json";

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

                if (!ProjectEntries.ContainsKey("MaintenanceMode"))
                {
                    ProjectEntries.Add("MaintenanceMode", maintenanceEntry);
                }
            }

            foreach (var project in projectConfig)
            {
                if (project.Key != "MaintenanceMode" && !ProjectEntries.ContainsKey(project.Key))
                {
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

                    ProjectEntries.Add(project.Key, new ProjectEntry
                    {
                        ProjectName = project.Key,
                        ProjectCurrentVersion = projectInfo.ProjectCurrentVersion ?? string.Empty,
                        ProjectVersion = version,
                        ProjectForceUpdate = forceUpdate
                    });
                }
            }

            foreach (var kvp in ProjectEntries)
            {
                if (kvp.Key == "MaintenanceMode")
                {
                    Log.Instance.Trace($"MaintenanceMode: {kvp.Value.MaintenanceMode}");
                }
                else
                {
                    Log.Instance.Trace($"Project: {kvp.Value.ProjectName}, Version: {kvp.Value.ProjectVersion}, Force Update: {kvp.Value.ProjectForceUpdate}");
                }
            }

            string projectName = Branch == "Dev" ? $"{projectInfo.ProjectName}Dev" : projectInfo.ProjectName;

            if (!ProjectEntries.ContainsKey(projectName))
            {
                Log.Instance.Trace($"Project entry '{projectName}' not found in configuration.");
                return (StatusCode.Null, string.Empty);
            }

            if (ProjectEntries[projectName].IsValid())
            {
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
                if (projectVersion == currentVersion)
                {
                    return (StatusCode.NoUpdate, ProjectEntries[projectName].ProjectVersion);
                }
            }

            return (StatusCode.Null, string.Empty);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Error fetching version from server: {ex.Message}");
            return (StatusCode.Null, string.Empty);
        }
    }

    private async Task<(Version?, Version?, StatusCode, ProjectInfo)> TryGetUpdateFromServer()
    {
        if (File.Exists("DevMode"))
        {
            _branch = "Dev";
        }

        var thisReleaseVersion = Assembly.GetEntryAssembly()?.GetName().Version;

        ProjectInfo projectInfo = new ProjectInfo();
        projectInfo.ProjectName = "LenovoLegionToolkit";
        projectInfo.ProjectExeName = "LenovoLegionToolkitSetup.exe";
        projectInfo.ProjectAuthor = "XKaguya";
        projectInfo.ProjectCurrentVersion = thisReleaseVersion?.ToString() ?? "0.0.0.0";
        projectInfo.ProjectCurrentExePath = "NULL";
        projectInfo.ProjectNewExePath = "NULL";

        var (statusCode, newestVersion) = await GetLatestVersionWithRetryAsync(projectInfo).ConfigureAwait(false);

        if (IsServerUnderMaintenanceMode() && Branch != "Dev")
        {
            Log.Instance.Trace($"Update Server is currently under maintenance mode.");
            Log.Instance.Trace($"Current branch is {Branch}");
            Log.Instance.Trace($"Now exiting...");

            Status = UpdateCheckStatus.Success;
            return (null, null, StatusCode.Null, new ProjectInfo());
        }

        projectInfo.ProjectNewVersion = newestVersion;
        var currentVersion = Version.Parse(projectInfo.ProjectCurrentVersion);
        var newVersion = Version.Parse(newestVersion);

        return (currentVersion, newVersion, statusCode, projectInfo);
    }
}
