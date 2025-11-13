using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
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
using System.Windows.Media;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public partial class FloatingGadgetUpper
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
    private bool _positionSet;
    private bool _fpsMonitoringStarted = false;

    public FloatingGadgetUpper()
    {
        InitializeComponent();

        IsVisibleChanged += FloatingGadget_IsVisibleChanged;
        SourceInitialized += OnSourceInitialized!;
        Closed += FloatingGadget_Closed!;
        Loaded += OnLoaded;
        ContentRendered += OnContentRendered;

        var mi = Compatibility.GetMachineInformationAsync().Result;
        if (mi.Properties.IsAmdDevice)
        {
            _pchName.Text = Resource.SensorsControl_Motherboard_Title;
        }

        MessagingCenter.Subscribe<FloatingGadgetChangedMessage>(this, (message) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (App.Current.FloatingGadget != null)
                {
                    if (message.State == FloatingGadgetState.Show)
                    {
                        App.Current.FloatingGadget.Show();
                    }
                    else
                    {
                        App.Current.FloatingGadget.Hide();
                    }
                }
            });
        });

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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(SetWindowPosition), DispatcherPriority.Loaded);
    }

    private void OnContentRendered(object sender, EventArgs e)
    {
        if (!_positionSet)
        {
            Dispatcher.BeginInvoke(new Action(SetWindowPosition), DispatcherPriority.Render);
        }
    }

    private void SetWindowPosition()
    {
        if (double.IsNaN(ActualWidth) || ActualWidth <= 0)
            return;

        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var workArea = SystemParameters.WorkArea;

        var left = (screenWidth - ActualWidth) / 2;
        var top = workArea.Top;

        Left = left - 35;
        Top = top;
        _positionSet = true;
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

    private void OnFpsDataUpdated(object? sender, FpsSensorController.FpsData fpsData)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdateFpsDisplay(fpsData.Fps, fpsData.LowFps, fpsData.FrameTime);
        }, DispatcherPriority.Normal);
    }

    private async Task StartFpsMonitoringAsync()
    {
        await _fpsController.StartMonitoringAsync();
    }

    public void UpdateSensorData(
        double cpuUsage, double cpuFrequency, double cpuTemp, double cpuPower,
        double gpuUsage, double gpuFrequency, double gpuTemp, double gpuVramTemp, double gpuPower,
        double memUsage, double pchTemp, double memTemp,
        int cpuFanSpeed, int gpuFanSpeed, int pchFanSpeed)
    {
        if ((DateTime.Now - _lastUpdate).TotalMilliseconds < 100) return;
        _lastUpdate = DateTime.Now;

        // CPU Frequency
        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0}MHz", cpuFrequency);
        SetTextIfChanged(_cpuFrequency, _stringBuilder.ToString());

        // CPU Usage
        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}%", cpuUsage);
        SetTextIfChanged(_cpuUsage, _stringBuilder.ToString());
        SetForegroundIfChanged(_cpuUsage, SeverityBrush(cpuUsage, 70, 90));

        // CPU Temperature
        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}°C", cpuTemp);
        SetTextIfChanged(_cpuTemperature, _stringBuilder.ToString());
        SetForegroundIfChanged(_cpuTemperature, SeverityBrush(cpuTemp, 75, 90));

        // CPU Power
        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F1}W", cpuPower);
        SetTextIfChanged(_cpuPower, _stringBuilder.ToString());

        // GPU Frequency
        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0}MHz", gpuFrequency);
        SetTextIfChanged(_gpuFrequency, _stringBuilder.ToString());

        // GPU Usage
        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}%", gpuUsage);
        SetTextIfChanged(_gpuUsage, _stringBuilder.ToString());
        SetForegroundIfChanged(_gpuUsage, SeverityBrush(gpuUsage, 70, 90));

        // GPU Temperature
        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}°C", gpuTemp);
        SetTextIfChanged(_gpuTemperature, _stringBuilder.ToString());
        SetForegroundIfChanged(_gpuTemperature, SeverityBrush(gpuTemp, 70, 80));

        // GPU VRAM Temperature
        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}°C", gpuVramTemp);
        SetTextIfChanged(_gpuVramTemperature, _stringBuilder.ToString());
        SetForegroundIfChanged(_gpuVramTemperature, SeverityBrush(gpuVramTemp, 70, 80));

        // GPU Power
        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F1}W", gpuPower);
        SetTextIfChanged(_gpuPower, _stringBuilder.ToString());

        // Memory Usage
        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}%", memUsage);
        SetTextIfChanged(_memUsage, _stringBuilder.ToString());
        SetForegroundIfChanged(_memUsage, SeverityBrush(memUsage, 75, 80));

        // Memory Temperature
        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}°C", memTemp);
        SetTextIfChanged(_memTemperature, _stringBuilder.ToString());
        SetForegroundIfChanged(_memTemperature, SeverityBrush(memTemp, 60, 75));

        // PCH Temperature
        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}°C", pchTemp);
        SetTextIfChanged(_pchTemperature, _stringBuilder.ToString());
        SetForegroundIfChanged(_pchTemperature, SeverityBrush(pchTemp, 60, 75));

        // Fan Speeds
        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0}RPM", cpuFanSpeed);
        SetTextIfChanged(_cpuFanSpeed, _stringBuilder.ToString());

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0}RPM", gpuFanSpeed);
        SetTextIfChanged(_gpuFanSpeed, _stringBuilder.ToString());

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0}RPM", pchFanSpeed);
        SetTextIfChanged(_pchFanSpeed, _stringBuilder.ToString());
    }

    private static Brush SeverityBrush(double value, double yellowThreshold, double redThreshold)
    {
        if (double.IsNaN(value)) return Brushes.White;
        if (value >= redThreshold) return Brushes.Red;
        if (value >= yellowThreshold) return Brushes.Goldenrod;
        return Brushes.White;
    }

    private static Brush FanBrush(int rpm)
    {
        if (rpm < 0) return Brushes.Red;
        if (rpm >= 6500) return Brushes.Goldenrod;
        return Brushes.White;
    }

    private static void SetTextIfChanged(System.Windows.Controls.TextBlock tb, string text)
    {
        if (!string.Equals(tb.Text, text, StringComparison.Ordinal))
            tb.Text = text;
    }

    private static void SetForegroundIfChanged(System.Windows.Controls.TextBlock tb, System.Windows.Media.Brush brush)
    {
        if (!Equals(tb.Foreground, brush))
            tb.Foreground = brush;
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

        var normalBrush = Brushes.White;
        var alertBrush = Brushes.Red;

        var fpsBrush = (fpsVal >= 0 && fpsVal < redLine) ? alertBrush : normalBrush;
        var lowFpsBrush = (lowVal >= 0 && fpsVal >= 0 && (fpsVal - lowVal) >= 30) ? alertBrush : normalBrush;
        var frameTimeBrush = (ftVal >= 0 && ftVal > maxFrameTime) ? alertBrush : normalBrush;

        if (!Equals(_fps.Foreground, fpsBrush))
            _fps.Foreground = fpsBrush;
        if (!Equals(_lowFps.Foreground, lowFpsBrush))
            _lowFps.Foreground = lowFpsBrush;
        if (!Equals(_frameTime.Foreground, frameTimeBrush))
            _frameTime.Foreground = frameTimeBrush;
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
                    await RefreshSensorsDataAsync(token);
                    await Task.Delay(TimeSpan.FromSeconds(_settings.Store.FloatingGadgetsRefreshInterval), token);
                }
                catch (Exception ex)
                {
                    if (Log.Instance.IsTraceEnabled)
                    {
                        Log.Instance.Trace($"Exception occur when executing TheRing()", ex);
                    }
                }
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

    private async Task RefreshSensorsDataAsync(CancellationToken token)
    {
        await _sensorsGroupControllers.UpdateAsync();

        var dataTask = _controller.GetDataAsync();
        var cpuPowerTask = _sensorsGroupControllers.GetCpuPowerAsync();
        var gpuPowerTask = _sensorsGroupControllers.GetGpuPowerAsync();
        var gpuVramTask = _sensorsGroupControllers.GetGpuVramTemperatureAsync();
        var memoryUsageTask = _sensorsGroupControllers.GetMemoryUsageAsync();
        var memoryTemperaturesTask = _sensorsGroupControllers.GetHighestMemoryTemperatureAsync();

        await Task.WhenAll(dataTask, cpuPowerTask, gpuPowerTask, gpuVramTask, memoryUsageTask, memoryTemperaturesTask);

        token.ThrowIfCancellationRequested();

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
            data.CPU.FanSpeed,
            data.GPU.FanSpeed,
            data.PCH.FanSpeed
        ), DispatcherPriority.Normal);
    }
}