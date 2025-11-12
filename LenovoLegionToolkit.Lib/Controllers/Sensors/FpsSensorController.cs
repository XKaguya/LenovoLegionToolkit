using LenovoLegionToolkit.Lib.Utils;
using PresentMonFps;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

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
        private readonly object _lockObject = new object();
        private bool _isRunning = false;
        private CancellationTokenSource? _currentProcessTokenSource;

        public event EventHandler<FpsData>? FpsDataUpdated;

        public async Task StartMonitoringAsync()
        {
            if (_isRunning) return;

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

                            if (currentProcess != null && !currentProcess.HasExited)
                            {
                                await StartProcessMonitoringAsync(currentProcess);
                                lastProcess = currentProcess;
                            }
                        }
                        else if (currentProcess == null && _currentMonitoredProcess != null)
                        {
                            StopProcessMonitoring();
                            lastProcess = null;
                        }
                        else if (currentProcess != null && _currentMonitoredProcess != null && currentProcess.Id == _currentMonitoredProcess.Id && _currentMonitoredProcess.HasExited)
                        {
                            StopProcessMonitoring();
                            lastProcess = null;
                        }

                        await Task.Delay(1000, _cancellationTokenSource.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (Log.Instance.IsTraceEnabled)
                        {
                            Log.Instance.Trace($"Monitoring loop error: {ex.Message}");
                        }
                        await Task.Delay(1000, _cancellationTokenSource.Token);
                    }
                }
            }, _cancellationTokenSource.Token);
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
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                    return null;

                GetWindowThreadProcessId(hwnd, out uint processId);
                if (processId == 0)
                    return null;

                if (processId == 0 || processId == 4)
                    return null;

                using var process = Process.GetProcessById((int)processId);

                if (process == null || string.IsNullOrEmpty(process.ProcessName) || process.HasExited)
                    return null;

                if (IsProcessBlacklisted(process.ProcessName))
                    return null;

                return Process.GetProcessById((int)processId);
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (Win32Exception) { return null; }
        }

        private async Task StartProcessMonitoringAsync(Process process)
        {
            try
            {
                _currentProcessTokenSource = new CancellationTokenSource();
                _currentMonitoredProcess = process;

                var request = new FpsRequest((uint)process.Id);
                var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    _currentProcessTokenSource.Token,
                    _cancellationTokenSource?.Token ?? CancellationToken.None);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await FpsInspector.StartForeverAsync(request, OnFpsDataReceived, linkedTokenSource.Token);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        if (Log.Instance.IsTraceEnabled)
                        {
                            Log.Instance.Trace($"Monitoring failed for {process.ProcessName}", ex);
                        }
                        lock (_lockObject)
                        {
                            if (_currentMonitoredProcess?.Id == process.Id)
                            {   
                                _currentMonitoredProcess = null;
                            }
                        }
                    }
                }, linkedTokenSource.Token);

                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"Started monitoring {process.ProcessName} (PID: {process.Id})");
                }
            }
            catch (Exception ex)
            {
                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"Failed to start monitoring for {process.ProcessName}", ex);
                }

                lock (_lockObject)
                {
                    _currentMonitoredProcess = null;
                }
            }
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
                        if (Log.Instance.IsTraceEnabled)
                        {
                            Log.Instance.Trace($"Stopped monitoring: {_currentMonitoredProcess.ProcessName}");
                        }
                        _currentMonitoredProcess = null;
                        _currentFpsData = new FpsData();
                    }
                }

                FpsDataUpdated?.Invoke(this, GetCurrentFpsData());
            }
            catch (Exception ex)
            {
                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"Error stopping process monitoring", ex);
                }
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