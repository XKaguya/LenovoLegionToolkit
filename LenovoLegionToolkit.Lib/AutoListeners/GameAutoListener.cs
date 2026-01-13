using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.GameDetection;
using LenovoLegionToolkit.Lib.Utils;
using Microsoft.Win32;
using NeoSmart.AsyncLock;

namespace LenovoLegionToolkit.Lib.AutoListeners;

public class GameAutoListener : AbstractAutoListener<GameAutoListener.ChangedEventArgs>
{
    public class ChangedEventArgs(bool running) : EventArgs
    {
        public bool Running { get; } = running;
    }

    private static readonly string WindowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string[] ProVendors =
    [
        "Adobe", "Autodesk", "Dassault", "Bentley", "Google", "JetBrains",
        "Microsoft Corporation", "NVIDIA Corporation", "Intel Corporation", "AMD"
    ];

    private static readonly string[] GameMarkers =
    [
        "steam_api64.dll", "steam_api.dll", "eossdk", "unityplayer.dll", "gameassembly.dll",
        "xinput", "vulkan-1.dll", "bink2w64.dll", "steam_appid.txt", "discord_game_sdk",
        "d3d11.dll", "d3d12.dll", "dxgi.dll", "PhysX", "AnselSDK64.dll", "Galaxy64.dll",
        "EA.GameData.dll", "EAAntiCheat", "uplay_r1_loader64.dll", "CryRender",
        "CryPhysics", "NvFlow", "FMOD", "AkSoundEngine"
    ];

    private readonly Dictionary<uint, string> _pidToPathMap = [];
    private readonly HashSet<string> _discoveredLibraryPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ProcessInfo> _detectedGamePathsCache = [];

    private ManagementEventWatcher? _startWatcher;
    private ManagementEventWatcher? _stopWatcher;
    private bool _lastState;
    private bool _isGameModeActive;
    private readonly AsyncLock _lock = new();

    private readonly InstanceStartedEventAutoAutoListener _instanceListener;
    private readonly GameConfigStoreDetector _gameConfigStoreDetector;
    private readonly EffectiveGameModeDetector _effectiveGameModeDetector;

    public GameAutoListener(InstanceStartedEventAutoAutoListener instanceListener)
    {
        _instanceListener = instanceListener;
        _gameConfigStoreDetector = new GameConfigStoreDetector();
        _effectiveGameModeDetector = new EffectiveGameModeDetector();

        _gameConfigStoreDetector.GamesDetected += (s, e) =>
        {
            using (_lock.Lock())
            {
                _detectedGamePathsCache.Clear();
                foreach (var g in e.Games) _detectedGamePathsCache.Add(g);
            }
        };

        _effectiveGameModeDetector.Changed += (s, e) =>
        {
            using (_lock.Lock())
            {
                _isGameModeActive = e;
                if (_pidToPathMap.Count == 0) RaiseChangedIfNeeded(e);
            }
        };

        InitializeLibraryPaths();
    }

    public bool AreGamesRunning()
    {
        using (_lock.Lock()) return _lastState;
    }

    protected override async Task StartAsync()
    {
        using (await _lock.LockAsync().ConfigureAwait(false))
        {
            foreach (var gamePath in GameConfigStoreDetector.GetDetectedGamePaths())
                _detectedGamePathsCache.Add(gamePath);
        }

        await _gameConfigStoreDetector.StartAsync().ConfigureAwait(false);
        await _effectiveGameModeDetector.StartAsync().ConfigureAwait(false);
        await _instanceListener.SubscribeChangedAsync(OnInstanceStarted).ConfigureAwait(false);

        _ = Task.Run(() =>
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    var path = proc.GetFileName();
                    if (string.IsNullOrEmpty(path)) continue;
                    EvaluateProcess((uint)proc.Id, proc.ProcessName, path, true);
                }
                catch { /* Ignore */ }
            }
        });

        await Task.Run(() =>
        {
            try
            {
                var startQuery = new WqlEventQuery("SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_Process'");
                _startWatcher = new ManagementEventWatcher(startQuery);
                _startWatcher.EventArrived += (s, e) =>
                {
                    if (e.NewEvent["TargetInstance"] is not ManagementBaseObject target) return;
                    uint pid = (uint)target["ProcessID"];
                    string? name = target["Name"]?.ToString();
                    string? path = target["ExecutablePath"]?.ToString();
                    if (string.IsNullOrEmpty(path)) try { path = Process.GetProcessById((int)pid).GetFileName(); } catch { /* Ignore */ }
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
            catch (Exception ex) { Log.Instance.Trace($"WMI failure", ex); }
        }).ConfigureAwait(false);
    }

    protected override async Task StopAsync()
    {
        await _instanceListener.UnsubscribeChangedAsync(OnInstanceStarted).ConfigureAwait(false);
        await _gameConfigStoreDetector.StopAsync().ConfigureAwait(false);
        await _effectiveGameModeDetector.StopAsync().ConfigureAwait(false);

        try { _startWatcher?.Stop(); _stopWatcher?.Stop(); } catch { /* Ignore */ }
        _startWatcher?.Dispose(); _stopWatcher?.Dispose();
        _startWatcher = null; _stopWatcher = null;

        using (await _lock.LockAsync().ConfigureAwait(false))
        {
            _pidToPathMap.Clear();
            _lastState = false;
        }
    }

    private void EvaluateProcess(uint pid, string? processName, string? path, bool isInitialScan = false)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path) || path.Contains("System.Char[]")) return;
        if (path.StartsWith(WindowsPath, StringComparison.OrdinalIgnoreCase)) return;

        bool isGame = false;
        try
        {
            if (!isInitialScan) Thread.Sleep(3000);

            if (_discoveredLibraryPaths.Any(lib => path.StartsWith(lib, StringComparison.OrdinalIgnoreCase)))
            {
                isGame = true;
            }

            if (!isGame)
            {
                using (_lock.Lock())
                {
                    if (_detectedGamePathsCache.Any(g =>
                        string.Equals(g.ExecutablePath, path, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(g.Name, processName, StringComparison.OrdinalIgnoreCase)))
                    {
                        isGame = true;
                    }
                }
            }

            if (!isGame)
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(path);
                var company = versionInfo.CompanyName ?? string.Empty;
                bool isPro = ProVendors.Any(v => company.Contains(v, StringComparison.OrdinalIgnoreCase));

                if (!isPro)
                {
                    isGame = IsGameViaShell(path) ||
                             HasGameNameHeuristic(processName ?? "") ||
                             HasDiskFingerprint(path) ||
                             HasMemoryFingerprint(pid);
                }
            }

            if (isGame) MarkAsGame(pid, path);
        }
        catch { /* Ignore */ }
    }

    private void MarkAsGame(uint pid, string path)
    {
        using (_lock.Lock())
        {
            if (_pidToPathMap.TryAdd(pid, path))
            {
                RaiseChangedIfNeeded(true);
            }
        }
    }

    private void HandleProcessExit(uint pid)
    {
        using (_lock.Lock())
        {
            if (_pidToPathMap.Remove(pid))
            {
                if (_pidToPathMap.Count == 0)
                {
                    RaiseChangedIfNeeded(_isGameModeActive);
                }
            }
        }
    }

    private void RaiseChangedIfNeeded(bool newState)
    {
        if (newState == _lastState) return;
        _lastState = newState;
        RaiseChanged(new ChangedEventArgs(newState));
    }

    private bool HasDiskFingerprint(string exePath)
    {
        try
        {
            var rootFolder = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(rootFolder) || !Directory.Exists(rootFolder)) return false;

            var searchPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = new DirectoryInfo(rootFolder);
            for (int i = 0; i < 6 && current != null; i++)
            {
                searchPaths.Add(current.FullName);
                current = current.Parent;
            }

            void AddChildren(string path, int depth)
            {
                if (depth <= 0) return;
                try
                {
                    foreach (var d in Directory.GetDirectories(path))
                    {
                        searchPaths.Add(d);
                        AddChildren(d, depth - 1);
                    }
                }
                catch { /* Ignore */ }
            }
            AddChildren(rootFolder, 3);

            foreach (var folder in searchPaths)
            {
                try
                {
                    var files = Directory.GetFiles(folder, "*.*").Select(Path.GetFileName);
                    if (files.Any(f => f != null && GameMarkers.Any(m => f.Contains(m, StringComparison.OrdinalIgnoreCase))))
                        return true;
                }
                catch { /* Ignore */ }
            }
        }
        catch { /* Ignore */ }
        return false;
    }

    private bool HasMemoryFingerprint(uint pid)
    {
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return proc.Modules.Cast<ProcessModule>()
                .Any(m => GameMarkers.Any(marker => m.ModuleName?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true));
        }
        catch { return false; }
    }

    private bool HasGameNameHeuristic(string name) =>
        name.Contains("-Win64-Shipping", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("-Win32-Shipping", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("Game", StringComparison.OrdinalIgnoreCase);

    private bool IsGameViaShell(string path)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return false;
            dynamic shell = Activator.CreateInstance(shellType)!;
            var folder = shell.NameSpace(Path.GetDirectoryName(path));
            var item = folder.ParseName(Path.GetFileName(path));
            string kind = folder.GetDetailsOf(item, 305) ?? "";
            return kind.Contains("Game", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private void OnInstanceStarted(object? sender, InstanceStartedEventAutoAutoListener.ChangedEventArgs e)
    {
        if (e.ProcessId <= 0) return;
        string? realPath = null;
        try { realPath = Process.GetProcessById(e.ProcessId).GetFileName(); } catch { /* Ignore */ }
        if (!string.IsNullOrEmpty(realPath)) EvaluateProcess((uint)e.ProcessId, e.ProcessName, realPath!);
    }

    private void InitializeLibraryPaths()
    {
        void AddPath(string? path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                _discoveredLibraryPaths.Add(path);
        }

        try
        {
            var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (!string.IsNullOrEmpty(steam))
            {
                var vdf = Path.Combine(steam!, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), @"""path""\s+""([^""]+)"""))
                        AddPath(Path.Combine(m.Groups[1].Value.Replace(@"\\", @"\"), "steamapps", "common"));
                }
            }
        }
        catch { /* Ignore */ }

        try
        {
            var epic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
            if (Directory.Exists(epic))
            {
                foreach (var f in Directory.GetFiles(epic, "*.item"))
                {
                    var m = Regex.Match(File.ReadAllText(f), @"""InstallLocation"":\s*""([^""]+)""");
                    if (m.Success) AddPath(m.Groups[1].Value.Replace(@"\\", @"\").TrimEnd('\\'));
                }
            }
        }
        catch { /* Ignore */ }

        try { AddPath(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Electronic Arts\EA Core", "EADesktopInstallPath", null) as string); } catch { /* Ignore */ }
        try { AddPath(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\GOG.com\GalaxyClient\paths", "library", null) as string); } catch { /* Ignore */ }
    }
}