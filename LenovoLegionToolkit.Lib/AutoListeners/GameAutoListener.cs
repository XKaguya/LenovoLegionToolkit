using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NeoSmart.AsyncLock;

namespace LenovoLegionToolkit.Lib.AutoListeners;

public class GameAutoListener : AbstractAutoListener<GameAutoListener.ChangedEventArgs>
{
    public class ChangedEventArgs(bool running) : EventArgs
    {
        public bool Running { get; } = running;
    }

    private readonly Dictionary<uint, string> _pidToIdentityMap = [];
    private readonly Dictionary<string, int> _activeIdentityCounts = [];
    private readonly HashSet<string> _discoveredLibraryPaths = new(StringComparer.OrdinalIgnoreCase);
    private ManagementEventWatcher? _startWatcher;
    private ManagementEventWatcher? _stopWatcher;
    private bool _lastState;
    private readonly AsyncLock _lock = new();

    private static readonly string WindowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string[] ProVendors =
    [
        "Adobe", "Autodesk", "Dassault", "Bentley", "Google", "JetBrains", "Microsoft Corporation"
    ];

    public GameAutoListener()
    {
        InitializeLibraryPaths();
    }

    public bool AreGamesRunning()
    {
        lock (_lock) return _lastState;
    }

    protected override async Task StartAsync()
    {
        _ = Task.Run(() =>
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    var path = proc.GetFileName();
                    if (!string.IsNullOrEmpty(path) && !path.Contains("System.Char[]"))
                        EvaluateProcess((uint)proc.Id, proc.ProcessName, path, true);
                }
                catch { /* Ignore */ }
            }
        });

        await Task.Run(() =>
        {
            try
            {
                var startQuery = new WqlEventQuery("SELECT * FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'");
                _startWatcher = new ManagementEventWatcher(startQuery);
                _startWatcher.EventArrived += (s, e) =>
                {
                    if (e.NewEvent["TargetInstance"] is not ManagementBaseObject target) return;
                    uint pid = (uint)target["ProcessID"];
                    string? name = target["Name"]?.ToString();
                    string? path = target["ExecutablePath"]?.ToString();
                    Task.Run(() => EvaluateProcess(pid, name, path));
                };

                var stopQuery = new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace");
                _stopWatcher = new ManagementEventWatcher(stopQuery);
                _stopWatcher.EventArrived += (s, e) =>
                {
                    uint pid = (uint)e.NewEvent.Properties["ProcessID"].Value;
                    HandleProcessExit(pid);
                };

                _startWatcher.Start();
                _stopWatcher.Start();
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"WMI failure", ex);
            }
        }).ConfigureAwait(false);
    }

    protected override async Task StopAsync()
    {
        await Task.Run(() =>
        {
            try { _startWatcher?.Stop(); _stopWatcher?.Stop(); } catch { }
            _startWatcher?.Dispose(); _stopWatcher?.Dispose();
            _startWatcher = null; _stopWatcher = null;
            lock (_lock)
            {
                _pidToIdentityMap.Clear();
                _activeIdentityCounts.Clear();
                _lastState = false;
            }
        }).ConfigureAwait(false);
    }

    private void EvaluateProcess(uint pid, string? processName, string? path, bool isInitialScan = false)
    {
        if (string.IsNullOrEmpty(path) || path.Contains("System.Char[]") || string.IsNullOrEmpty(processName)) return;
        if (path.StartsWith(WindowsPath, StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            if (!isInitialScan) Thread.Sleep(3000);

            bool isLib = _discoveredLibraryPaths.Any(lib => path.StartsWith(lib, StringComparison.OrdinalIgnoreCase));
            bool isShell = false;
            bool isDisk = false;
            bool isName = false;
            bool isMem = false;

            if (!isLib)
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(path);
                var company = versionInfo.CompanyName ?? string.Empty;
                if (ProVendors.Any(v => company.Contains(v, StringComparison.OrdinalIgnoreCase))) return;

                isShell = IsGameViaShell(path);
                isDisk = HasDiskFingerprint(path);
                isName = HasGameNameHeuristic(processName);

                if (!isShell && !isDisk && !isName)
                {
                    using var process = Process.GetProcessById((int)pid);
                    isMem = HasGameFingerprint(process);
                }
            }

            if (isLib || isShell || isDisk || isName || isMem)
            {
                using var proc = Process.GetProcessById((int)pid);
                var title = proc.MainWindowTitle;
                var identity = string.IsNullOrWhiteSpace(title) ? processName : title;
                MarkAsGame(pid, identity);
            }
        }
        catch { /* Ignore */ }
    }

    private void MarkAsGame(uint pid, string identity)
    {
        lock (_lock)
        {
            if (!_pidToIdentityMap.TryAdd(pid, identity)) return;
            if (!_activeIdentityCounts.TryAdd(identity, 1)) _activeIdentityCounts[identity]++;
            RaiseChangedIfNeeded(true);
        }
    }

    private void HandleProcessExit(uint pid)
    {
        lock (_lock)
        {
            if (!_pidToIdentityMap.Remove(pid, out var identity)) return;
            if (_activeIdentityCounts.TryGetValue(identity, out var count))
            {
                if (count <= 1) _activeIdentityCounts.Remove(identity);
                else _activeIdentityCounts[identity] = count - 1;
            }
            if (_activeIdentityCounts.Count == 0) RaiseChangedIfNeeded(false);
        }
    }

    private void RaiseChangedIfNeeded(bool newState)
    {
        if (newState == _lastState) return;
        _lastState = newState;
        RaiseChanged(new ChangedEventArgs(newState));
    }

    private bool HasGameNameHeuristic(string processName)
    {
        return processName.Contains("-Win64-Shipping", StringComparison.OrdinalIgnoreCase) ||
               processName.Contains("-Win32-Shipping", StringComparison.OrdinalIgnoreCase);
    }

    private bool HasGameFingerprint(Process process)
    {
        try
        {
            var modules = process.Modules.Cast<ProcessModule>().Select(m => m.ModuleName?.ToLower() ?? string.Empty);
            string[] gameModules = ["steam_api", "eossdk", "unityplayer.dll", "gameassembly.dll", "xinput", "vulkan-1.dll"];
            return modules.Any(m => gameModules.Any(g => g.Contains(m)));
        }
        catch { return false; }
    }

    private bool HasDiskFingerprint(string exePath)
    {
        try
        {
            var currentFolder = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(currentFolder) || !Directory.Exists(currentFolder)) return false;

            string[] gameMarkers = ["steam_api64.dll", "steam_api.dll", "eossdk", "unityplayer.dll", "gameassembly.dll", "xinput", "vulkan-1.dll", "bink2w64.dll", "steam_appid.txt", "discord_game_sdk"];
            var foldersToSearch = new List<string> { currentFolder };
            var parent = Directory.GetParent(currentFolder);
            if (parent != null)
            {
                foldersToSearch.Add(parent.FullName);
                if (parent.Parent != null) foldersToSearch.Add(parent.Parent.FullName);
            }

            foreach (var folder in foldersToSearch)
            {
                try
                {
                    var files = Directory.GetFiles(folder, "*.*").Select(Path.GetFileName).ToList();
                    foreach (var marker in gameMarkers)
                    {
                        if (files.Any(f => f != null && f.Contains(marker, StringComparison.OrdinalIgnoreCase))) return true;
                    }
                }
                catch { continue; }
            }
        }
        catch { /* Ignore */ }
        return false;
    }

    private bool IsGameViaShell(string path)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return false;
            dynamic shell = Activator.CreateInstance(shellType)!;
            var folder = shell.NameSpace(Path.GetDirectoryName(path));
            var item = folder.ParseName(Path.GetFileName(path));
            string kind = folder.GetDetailsOf(item, 305) ?? string.Empty;
            return kind.Equals("Game", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private void InitializeLibraryPaths()
    {
        try
        {
            var steamPath = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (!string.IsNullOrEmpty(steamPath))
            {
                var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    var matches = Regex.Matches(File.ReadAllText(vdf), @"""path""\s+""([^""]+)""");
                    foreach (Match m in matches)
                    {
                        if (m.Groups.Count > 1)
                            _discoveredLibraryPaths.Add(Path.Combine(m.Groups[1].Value.Replace(@"\\", @"\"), "steamapps", "common"));
                    }
                }
            }
        }
        catch { /* Ignore */ }

        try
        {
            var epicManifestPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
            if (Directory.Exists(epicManifestPath))
            {
                foreach (var file in Directory.GetFiles(epicManifestPath, "*.item"))
                {
                    var match = Regex.Match(File.ReadAllText(file), @"""InstallLocation"":\s*""([^""]+)""");
                    if (match is { Success: true, Groups.Count: > 1 })
                        _discoveredLibraryPaths.Add(match.Groups[1].Value.Replace(@"\\", @"\").TrimEnd('\\'));
                }
            }
        }
        catch { /* Ignore */ }
    }
}