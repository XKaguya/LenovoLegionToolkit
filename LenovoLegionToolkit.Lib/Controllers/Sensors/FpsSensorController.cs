using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using PresentMonFps;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors
{
    public class FpsSensorController : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public class FpsData
        {
            public string Fps { get; set; } = "-1";
            public string LowFps { get; set; } = "-1";
            public string FrameTime { get; set; } = "-1";
            public override string ToString() => $"FPS: {Fps}, Low: {LowFps}, Time: {FrameTime}ms";
        }

        public List<string> Blacklist = new List<string>();

        private FpsData _currentFpsData = new FpsData();
        private CancellationTokenSource? _cancellationTokenSource;
        private Process? _currentMonitoredProcess;
        private readonly Lock _lockObject = new Lock();
        private bool _isRunning = false;
        private CancellationTokenSource? _currentProcessTokenSource;

        public event EventHandler<FpsData>? FpsDataUpdated;

        public void InitializeBlacklist()
        {
            var systemProcesses = new[]
            {
                "explorer", "taskmgr", "ApplicationFrameHost", "System",
                "svchost", "csrss", "wininit", "services", "lsass",
                "winlogon", "smss", "spoolsv", "SearchIndexer", "SearchUI",
                "RuntimeBroker", "dwm", "ctfmon", "audiodg", "fontdrvhost",
                "taskhost", "conhost", "sihost", "StartMenuExperienceHost",
                "ShellExperienceHost", "Lenovo Legion Toolkit"
            };

            foreach (var process in systemProcesses)
            {
                if (!Blacklist.Contains(process))
                {
                    Blacklist.Add(process);
                }
            }
        }

        public Task StartMonitoringAsync()
        {
            if (_isRunning) return Task.CompletedTask;

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                Process? lastProcess = null;

                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        var currentProcess = GetForegroundProcess();

                        if (currentProcess != null && currentProcess.Id != lastProcess?.Id)
                        {
                            StopProcessMonitoring();

                            if (!currentProcess.HasExited)
                            {
                                await StartProcessMonitoringAsync(currentProcess).ConfigureAwait(false);
                                lastProcess = currentProcess;
                            }
                        }
                        else if (currentProcess == null && _currentMonitoredProcess != null || currentProcess != null && _currentMonitoredProcess != null && currentProcess.Id == _currentMonitoredProcess.Id && _currentMonitoredProcess.HasExited)
                        {
                            StopProcessMonitoring();
                            lastProcess = null;
                        }

                        await Task.Delay(1000, _cancellationTokenSource.Token).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log.Instance.Trace($"Monitoring loop error: {ex.Message}");
                        await Task.Delay(1000, _cancellationTokenSource.Token).ConfigureAwait(false);
                    }
                }
            }, _cancellationTokenSource.Token);

            return Task.CompletedTask;
        }

        public void StopMonitoring()
        {
            _isRunning = false;
            _cancellationTokenSource?.Cancel();
            StopProcessMonitoring();
        }

        public FpsData GetCurrentFpsData()
        {
            lock (_lockObject)
            {
                return new FpsData
                {
                    Fps = _currentFpsData.Fps,
                    LowFps = _currentFpsData.LowFps,
                    FrameTime = _currentFpsData.FrameTime
                };
            }
        }

        private Process? GetForegroundProcess()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                    return null;

                GetWindowThreadProcessId(hwnd, out var processId);
                switch (processId)
                {
                    case 0:
                    case 4:
                        return null;
                }

                using var process = Process.GetProcessById((int)processId);

                if (string.IsNullOrEmpty(process.ProcessName) || process.HasExited)
                    return null;

                return IsProcessBlacklisted(process.ProcessName) ? null : Process.GetProcessById((int)processId);
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (Win32Exception) { return null; }
        }

        private Task StartProcessMonitoringAsync(Process process)
        {
            try
            {
                _currentProcessTokenSource = new CancellationTokenSource();
                _currentMonitoredProcess = process;

                var request = new FpsRequest((uint)process.Id);
                var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    _currentProcessTokenSource.Token,
                    _cancellationTokenSource?.Token ?? CancellationToken.None);

                var monitoringTask = Task.Run(async () =>
                {
                    await FpsInspector.StartForeverAsync(request, OnFpsDataReceived, linkedTokenSource.Token).ConfigureAwait(true);
                }, linkedTokenSource.Token);

                monitoringTask.ContinueWith(t =>
                {
                    if (t.IsCanceled)
                    {
                        return;
                    }

                    if (!t.IsFaulted)
                    {
                        return;
                    }

                    var ex = t.Exception?.Flatten().InnerException ?? t.Exception;

                    Log.Instance.Trace($"Monitoring failed for {process.ProcessName}", ex!);

                    lock (_lockObject)
                    {
                        if (_currentMonitoredProcess?.Id == process.Id)
                        {
                            _currentMonitoredProcess = null;
                        }
                    }
                }, TaskContinuationOptions.ExecuteSynchronously);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to start monitoring for {process.ProcessName}", ex);

                lock (_lockObject)
                {
                    _currentMonitoredProcess = null;
                }
            }

            return Task.CompletedTask;
        }

        private void StopProcessMonitoring()
        {
            try
            {
                _currentProcessTokenSource?.Cancel();
                _currentProcessTokenSource?.Dispose();
                _currentProcessTokenSource = null;

                lock (_lockObject)
                {
                    if (_currentMonitoredProcess != null)
                    {
                        _currentMonitoredProcess = null;
                        _currentFpsData = new FpsData();
                    }
                }

                FpsDataUpdated?.Invoke(this, GetCurrentFpsData());
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Error stopping process monitoring", ex);
            }
        }

        private void OnFpsDataReceived(FpsResult result)
        {
            var fpsData = new FpsData
            {
                Fps = $"{result.Fps:0}",
                LowFps = $"{result.OnePercentLowFps:0}",
                FrameTime = $"{result.FrameTime:0.0}"
            };

            lock (_lockObject)
            {
                _currentFpsData = fpsData;
            }

            FpsDataUpdated?.Invoke(this, fpsData);
        }

        private bool IsProcessBlacklisted(string processName)
        {
            return Blacklist?.Any(x => string.Equals(processName, x, StringComparison.OrdinalIgnoreCase)) == true;
        }

        public void Dispose()
        {
            StopMonitoring();
            _cancellationTokenSource?.Dispose();
            _currentProcessTokenSource?.Dispose();
        }
    }
}