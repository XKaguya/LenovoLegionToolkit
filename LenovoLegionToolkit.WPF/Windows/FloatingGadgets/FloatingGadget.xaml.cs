using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public partial class FloatingGadget
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly SensorsController _controller = IoCContainer.Resolve<SensorsController>();
    private readonly SensorsGroupController _sensorsGroupControllers = IoCContainer.Resolve<SensorsGroupController>();
    private readonly FpsSensorController _fpsController = IoCContainer.Resolve<FpsSensorController>();

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly StringBuilder _stringBuilder = new(64);
    private DateTime _lastUpdate = DateTime.MinValue;

    private CancellationTokenSource? _cts;
    private bool _fpsMonitoringStarted = false;

    public FloatingGadget()
    {
        InitializeComponent();

        IsVisibleChanged += FloatingGadget_IsVisibleChanged;
        SourceInitialized += OnSourceInitialized!;
        Closed += FloatingGadget_Closed!;

        var mi = Compatibility.GetMachineInformationAsync().Result;
        if (mi.Properties.IsAmdDevice)
        {
            _pchName.Text = Resource.SensorsControl_Motherboard_Temperature;
        }

        InitializeFpsSensor();
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private void InitializeFpsSensor()
    {
        _fpsController.Blacklist.Add("explorer");
        _fpsController.Blacklist.Add("taskmgr");
        _fpsController.Blacklist.Add("ApplicationFrameHost");

        _fpsController.Blacklist.Add("System");
        _fpsController.Blacklist.Add("svchost");
        _fpsController.Blacklist.Add("csrss");
        _fpsController.Blacklist.Add("wininit");
        _fpsController.Blacklist.Add("services");
        _fpsController.Blacklist.Add("lsass");
        _fpsController.Blacklist.Add("winlogon");
        _fpsController.Blacklist.Add("smss");

        _fpsController.Blacklist.Add("spoolsv");
        _fpsController.Blacklist.Add("SearchIndexer");
        _fpsController.Blacklist.Add("SearchUI");
        _fpsController.Blacklist.Add("RuntimeBroker");
        _fpsController.Blacklist.Add("dwm");
        _fpsController.Blacklist.Add("ctfmon");
        _fpsController.Blacklist.Add("audiodg");
        _fpsController.Blacklist.Add("fontdrvhost");

        _fpsController.Blacklist.Add("taskhost");
        _fpsController.Blacklist.Add("conhost");
        _fpsController.Blacklist.Add("sihost");
        _fpsController.Blacklist.Add("StartMenuExperienceHost");
        _fpsController.Blacklist.Add("ShellExperienceHost");

        _fpsController.FpsDataUpdated += OnFpsDataUpdated;
    }

    private void OnSourceInitialized(object sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
    }

    private async Task StartFpsMonitoringAsync()
    {
        await _fpsController.StartMonitoringAsync();
    }

    private async void FloatingGadget_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _cts?.Cancel();
            _cts?.Dispose();

            _cts = new CancellationTokenSource();

            if (!_fpsMonitoringStarted)
            {
                await StartFpsMonitoringAsync();
                _fpsMonitoringStarted = true;
            }

            await TheRing(_cts.Token);
        }
        else
        {
            _cts?.Cancel();
        }
    }

    private void FloatingGadget_Closed(object sender, EventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _refreshLock.Dispose();

        _fpsController.FpsDataUpdated -= OnFpsDataUpdated;
        _fpsController.Dispose();
    }

    public void UpdateSensorData(
        double cpuUsage, double cpuFrequency, double cpuTemp, double cpuPower,
        double gpuUsage, double gpuFrequency, double gpuTemp, double gpuVramTemp, double gpuPower,
        double memUsage, double pchTemp, double memTemp, double disk0Temperature, double disk1Temperature,
        int cpuFanSpeed, int gpuFanSpeed, int pchFanSpeed)
    {
        if ((DateTime.Now - _lastUpdate).TotalMilliseconds < 100) return;
        _lastUpdate = DateTime.Now;

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}%", cpuUsage);
        _cpuUsage.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0}Mhz", cpuFrequency);
        _cpuFrequency.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}°C", cpuTemp);
        _cpuTemperature.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F1} W", cpuPower);
        _cpuPower.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}%", gpuUsage);
        _gpuUsage.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0}Mhz", gpuFrequency);
        _gpuFrequency.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}°C", gpuTemp);
        _gpuTemperature.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}°C", gpuVramTemp);
        _gpuVramTemperature.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F1} W", gpuPower);
        _gpuPower.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}%", memUsage);
        _memUsage.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}°C", pchTemp);
        _pchTemperature.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}°C", memTemp);
        _memTemperature.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}°C", disk0Temperature);
        _disk0Temperature.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}°C", disk1Temperature);
        _disk1Temperature.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0} RPM", cpuFanSpeed);
        _cpuFanSpeed.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0} RPM", gpuFanSpeed);
        _gpuFanSpeed.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0} RPM", pchFanSpeed);
        _pchFanSpeed.Text = _stringBuilder.ToString();
    }

    public async Task TheRing(CancellationToken token)
    {
        if (!await _refreshLock.WaitAsync(0, token))
            return;

        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await RefreshDataAsync(token);
                    await Task.Delay(TimeSpan.FromSeconds(_settings.Store.FloatingGadgetsRefreshInterval), token);
                }
                catch (Exception) { }
            }
        }
        finally
        {
            try
            {
                _refreshLock.Release();
            }
            catch (ObjectDisposedException) { }
        }
    }

    private void OnFpsDataUpdated(object? sender, FpsSensorController.FpsData fpsData)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdateFpsDisplay(fpsData.Fps, fpsData.LowFps, fpsData.FrameTime);
        }, DispatcherPriority.Normal);
    }

    private void UpdateFpsDisplay(string fps, string lowFps, string frameTime)
    {
        const string dash = "-";
        const int redLine = 30;
        const double maxFrameTime = 10.0;

        var fpsText = dash;
        int fpsVal = -1;
        if (int.TryParse(fps?.Trim(), out var fv) && fv >= 0)
        {
            fpsVal = fv;
            fpsText = fv.ToString();
        }

        var lowFpsText = dash;
        int lowVal = -1;
        if (int.TryParse(lowFps?.Trim(), out var lv) && lv >= 0)
        {
            lowVal = lv;
            lowFpsText = lv.ToString();
        }

        var frameTimeText = dash;
        double ftVal = -1;
        if (double.TryParse(frameTime?.Trim(), out var ft) && ft >= 0)
        {
            ftVal = ft;
            frameTimeText = $"{ft:F1}ms";
        }

        if (!string.Equals(_fps.Text, fpsText, StringComparison.Ordinal))
            _fps.Text = fpsText;
        if (!string.Equals(_lowFps.Text, lowFpsText, StringComparison.Ordinal))
            _lowFps.Text = lowFpsText;
        if (!string.Equals(_frameTime.Text, frameTimeText, StringComparison.Ordinal))
            _frameTime.Text = frameTimeText;

        var normalBrush = System.Windows.Media.Brushes.White;
        var alertBrush = System.Windows.Media.Brushes.Red;

        var fpsBrush = (fpsVal >= 0 && fpsVal < redLine) ? alertBrush : normalBrush;
        var lowFpsBrush = (lowVal >= 0 && lowVal < (fpsVal - redLine)) ? alertBrush : normalBrush;
        var frameTimeBrush = (ftVal >= 0 && ftVal > maxFrameTime) ? alertBrush : normalBrush;

        if (!Equals(_fps.Foreground, fpsBrush))
            _fps.Foreground = fpsBrush;
        if (!Equals(_lowFps.Foreground, lowFpsBrush))
            _lowFps.Foreground = lowFpsBrush;
        if (!Equals(_frameTime.Foreground, frameTimeBrush))
            _frameTime.Foreground = frameTimeBrush;
    }

    private async Task RefreshDataAsync(CancellationToken token)
    {
        await _sensorsGroupControllers.UpdateAsync();

        var dataTask = _controller.GetDataAsync();
        var cpuPowerTask = _sensorsGroupControllers.GetCpuPowerAsync();
        var gpuPowerTask = _sensorsGroupControllers.GetGpuPowerAsync();
        var gpuVramTask = _sensorsGroupControllers.GetGpuVramTemperatureAsync();
        var diskTemperaturesTask = _sensorsGroupControllers.GetSSDTemperaturesAsync();
        var memoryUsageTask = _sensorsGroupControllers.GetMemoryUsageAsync();
        var memoryTemperaturesTask = _sensorsGroupControllers.GetHighestMemoryTemperatureAsync();

        await Task.WhenAll(dataTask, cpuPowerTask, gpuPowerTask, gpuVramTask,
            diskTemperaturesTask, memoryUsageTask, memoryTemperaturesTask);

        var data = dataTask.Result;
        var cpuPower = cpuPowerTask.Result;
        var gpuPower = gpuPowerTask.Result;

        await Dispatcher.BeginInvoke(() => UpdateSensorData(
            data.CPU.Utilization,
            data.CPU.CoreClock,
            data.CPU.Temperature,
            cpuPower,
            data.GPU.Utilization,
            data.GPU.CoreClock,
            data.GPU.Temperature,
            gpuVramTask.Result,
            gpuPower,
            memoryUsageTask.Result,
            data.PCH.Temperature,
            memoryTemperaturesTask.Result,
            diskTemperaturesTask.Result.Item1,
            diskTemperaturesTask.Result.Item2,
            data.CPU.FanSpeed,
            data.GPU.FanSpeed,
            data.PCH.FanSpeed
        ), DispatcherPriority.Background);
    }
}