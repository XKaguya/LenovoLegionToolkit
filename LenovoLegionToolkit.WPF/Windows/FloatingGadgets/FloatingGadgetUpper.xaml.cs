using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public partial class FloatingGadgetUpper
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private const int UI_UPDATE_THROTTLE_MS = 100;

    private const int FpsRedLine = 30;
    private const double MaxFrameTimeMs = 10.0;

    private const double UsageYellow = 70;
    private const double UsageRed = 90;
    private const double MemUsageYellow = 75;
    private const double MemUsageRed = 80;

    private const double CpuTempYellow = 75;
    private const double CpuTempRed = 90;
    private const double GpuTempYellow = 70;
    private const double GpuTempRed = 80;
    private const double MemTempYellow = 60;
    private const double MemTempRed = 75;
    private const double PchTempYellow = 60;
    private const double PchTempRed = 75;

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

    private HashSet<FloatingGadgetItem> _activeItems = new();
    private List<FloatingGadgetItem> _visibleItems = new();
    private static Dictionary<FrameworkElement, (List<FloatingGadgetItem> Items, Rectangle? Separator)> GadgetGroups { get; } =
        new Dictionary<FrameworkElement, (List<FloatingGadgetItem> Items, Rectangle? Separator)>();

    private static Dictionary<FloatingGadgetItem, FrameworkElement> _itemsMap { get; } =
        new Dictionary<FloatingGadgetItem, FrameworkElement>();

    public FloatingGadgetUpper()
    {
        InitializeComponent();

        IsVisibleChanged += FloatingGadget_IsVisibleChanged;
        SourceInitialized += OnSourceInitialized!;
        Closed += FloatingGadget_Closed!;
        Loaded += OnLoaded;
        ContentRendered += OnContentRendered;

        InitializeComponentSpecifics();
        InitializeMappings();
        SubscribeEvents();
        InitializeFpsSensor();

        _activeItems = new HashSet<FloatingGadgetItem>(_settings.Store.FloatingGadgetItems);
        _visibleItems.AddRange(_activeItems);

        UpdateGadgetControlsVisibility();
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private void InitializeComponentSpecifics()
    {
        var mi = Compatibility.GetMachineInformationAsync().Result;
        if (mi.Properties.IsAmdDevice)
        {
            _pchName.Text = Resource.SensorsControl_Motherboard_Title;
        }
    }

    private void InitializeMappings()
    {
        if (_settings.Store.FloatingGadgetItems.Count == 0)
        {
            _settings.Store.FloatingGadgetItems = Enum.GetValues(typeof(FloatingGadgetItem)).Cast<FloatingGadgetItem>().ToList();
            _settings.SynchronizeStore();
        }

        if (GadgetGroups.Count == 0)
        {
            GadgetGroups.Add(_fpsGroup, (new List<FloatingGadgetItem> { FloatingGadgetItem.Fps, FloatingGadgetItem.LowFps, FloatingGadgetItem.FrameTime }, _separatorFps));
            GadgetGroups.Add(_cpuGroup, (new List<FloatingGadgetItem> { FloatingGadgetItem.CpuUtilization, FloatingGadgetItem.CpuFrequency, FloatingGadgetItem.CpuTemperature, FloatingGadgetItem.CpuPower, FloatingGadgetItem.CpuFan }, _separatorCpu));
            GadgetGroups.Add(_gpuGroup, (new List<FloatingGadgetItem> { FloatingGadgetItem.GpuUtilization, FloatingGadgetItem.GpuFrequency, FloatingGadgetItem.GpuTemperature, FloatingGadgetItem.GpuVramTemperature, FloatingGadgetItem.GpuPower, FloatingGadgetItem.GpuFan }, _separatorGpu));
            GadgetGroups.Add(_memoryGroup, (new List<FloatingGadgetItem> { FloatingGadgetItem.MemoryUtilization, FloatingGadgetItem.MemoryTemperature }, _separatorMemory));
            GadgetGroups.Add(_pchGroup, (new List<FloatingGadgetItem> { FloatingGadgetItem.PchTemperature, FloatingGadgetItem.PchFan }, null));
        }

        if (_itemsMap.Count == 0)
        {
            _itemsMap.Add(FloatingGadgetItem.Fps, _fps);
            _itemsMap.Add(FloatingGadgetItem.LowFps, _lowFps);
            _itemsMap.Add(FloatingGadgetItem.FrameTime, _frameTime);
            _itemsMap.Add(FloatingGadgetItem.CpuUtilization, _cpuUsage);
            _itemsMap.Add(FloatingGadgetItem.CpuFrequency, _cpuFrequency);
            _itemsMap.Add(FloatingGadgetItem.CpuTemperature, _cpuTemperature);
            _itemsMap.Add(FloatingGadgetItem.CpuPower, _cpuPower);
            _itemsMap.Add(FloatingGadgetItem.GpuUtilization, _gpuUsage);
            _itemsMap.Add(FloatingGadgetItem.GpuFrequency, _gpuFrequency);
            _itemsMap.Add(FloatingGadgetItem.GpuTemperature, _gpuTemperature);
            _itemsMap.Add(FloatingGadgetItem.GpuVramTemperature, _gpuVramTemperature);
            _itemsMap.Add(FloatingGadgetItem.GpuPower, _gpuPower);
            _itemsMap.Add(FloatingGadgetItem.MemoryUtilization, _memUsage);
            _itemsMap.Add(FloatingGadgetItem.MemoryTemperature, _memTemperature);
            _itemsMap.Add(FloatingGadgetItem.PchTemperature, _pchTemperature);
            _itemsMap.Add(FloatingGadgetItem.CpuFan, _cpuFanSpeed);
            _itemsMap.Add(FloatingGadgetItem.GpuFan, _gpuFanSpeed);
            _itemsMap.Add(FloatingGadgetItem.PchFan, _pchFanSpeed);
        }
    }

    private void SubscribeEvents()
    {
        MessagingCenter.Subscribe<FloatingGadgetChangedMessage>(this, (message) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (App.Current.FloatingGadget == null) return;

                if (message.State == FloatingGadgetState.Show)
                {
                    App.Current.FloatingGadget.Show();
                }
                else
                {
                    App.Current.FloatingGadget.Hide();
                }
            });
        });

        MessagingCenter.Subscribe<FloatingGadgetElementChangedMessage>(this, (message) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (App.Current.FloatingGadget == null) return;

                var newItemsSet = new HashSet<FloatingGadgetItem>(message.Items);
                if (!_activeItems.SetEquals(newItemsSet))
                {
                    _visibleItems.Clear();
                    _visibleItems.AddRange(message.Items);

                    _activeItems = newItemsSet;
                    UpdateGadgetControlsVisibility();
                }
            });
        });
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(SetWindowPosition), DispatcherPriority.Loaded);
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        if (!_positionSet)
        {
            Dispatcher.BeginInvoke(new Action(SetWindowPosition), DispatcherPriority.Render);
        }
    }

    private async void FloatingGadget_IsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
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

            UpdateGadgetControlsVisibility();

            await TheRing(_cts.Token);
        }
        else
        {
            _cts?.Cancel();
        }
    }

    private void FloatingGadget_Closed(object? sender, EventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _refreshLock.Dispose();

        _fpsController.FpsDataUpdated -= OnFpsDataUpdated;
        _fpsController.Dispose();
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

    private void UpdateGadgetGroupVisibility()
    {
        var visibleGroups = new List<FrameworkElement>();
        var allGroups = GadgetGroups.ToList();

        foreach (var kvp in allGroups)
        {
            var groupPanel = kvp.Key;
            var (items, separator) = kvp.Value;

            var isGroupActive = items.Any(item => _activeItems.Contains(item));

            groupPanel.Visibility = isGroupActive ? Visibility.Visible : Visibility.Collapsed;

            if (isGroupActive)
            {
                visibleGroups.Add(groupPanel);
            }
        }

        for (int i = 0; i < allGroups.Count; i++)
        {
            var (_, separator) = allGroups[i].Value;

            if (separator != null)
            {
                bool isCurrentGroupVisible = visibleGroups.Contains(allGroups[i].Key);

                if (isCurrentGroupVisible)
                {
                    int indexInVisible = visibleGroups.IndexOf(allGroups[i].Key);
                    bool nextGroupIsVisible = indexInVisible < visibleGroups.Count - 1;

                    separator.Visibility = nextGroupIsVisible ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    separator.Visibility = Visibility.Collapsed;
                }
            }
        }
    }

    private void UpdateGadgetControlsVisibility()
    {
        foreach (var kv in _itemsMap)
        {
            var control = kv.Value;
            control.Visibility = _activeItems.Contains(kv.Key) ? Visibility.Visible : Visibility.Collapsed;
        }

        UpdateGadgetGroupVisibility();
    }

    private void UpdateTextBlock(System.Windows.Controls.TextBlock tb, double value, string format, double yellowThreshold, double redThreshold)
    {
        if (tb.Visibility != Visibility.Visible) return;

        _stringBuilder.Clear();
        if (double.IsNaN(value) || value < 0)
        {
            SetTextIfChanged(tb, "-");
            SetForegroundIfChanged(tb, Brushes.White);
        }
        else
        {
            _stringBuilder.AppendFormat(format, value);
            SetTextIfChanged(tb, _stringBuilder.ToString());
            SetForegroundIfChanged(tb, SeverityBrush(value, yellowThreshold, redThreshold));
        }
    }
    private void UpdateTextBlock(System.Windows.Controls.TextBlock tb, double value, string format)
    {
        if (tb.Visibility != Visibility.Visible) return;

        _stringBuilder.Clear();
        if (double.IsNaN(value) || value < 0)
        {
            SetTextIfChanged(tb, "-");
        }
        else
        {
            _stringBuilder.AppendFormat(format, value);
            SetTextIfChanged(tb, _stringBuilder.ToString());
        }
    }
    private void UpdateTextBlock(System.Windows.Controls.TextBlock tb, int value)
    {
        if (tb.Visibility != Visibility.Visible) return;

        _stringBuilder.Clear();
        if (value < 0)
        {
            SetTextIfChanged(tb, "-");
        }
        else
        {
            _stringBuilder.AppendFormat("{0}RPM", value);
            SetTextIfChanged(tb, _stringBuilder.ToString());
        }
    }
    public void UpdateSensorData(
        double cpuUsage, double cpuFrequency, double cpuTemp, double cpuPower,
        double gpuUsage, double gpuFrequency, double gpuTemp, double gpuVramTemp, double gpuPower,
        double memUsage, double pchTemp, double memTemp,
        int cpuFanSpeed, int gpuFanSpeed, int pchFanSpeed)
    {
        // CPU
        UpdateTextBlock(_cpuFrequency, cpuFrequency, "{0}MHz");
        UpdateTextBlock(_cpuUsage, cpuUsage, "{0:F0}%", UsageYellow, UsageRed);
        UpdateTextBlock(_cpuTemperature, cpuTemp, "{0:F0}°C", CpuTempYellow, CpuTempRed);
        UpdateTextBlock(_cpuPower, cpuPower, "{0:F1}W");
        UpdateTextBlock(_cpuFanSpeed, cpuFanSpeed);

        // GPU
        UpdateTextBlock(_gpuFrequency, gpuFrequency, "{0}MHz");
        UpdateTextBlock(_gpuUsage, gpuUsage, "{0:F0}%", UsageYellow, UsageRed);
        UpdateTextBlock(_gpuTemperature, gpuTemp, "{0:F0}°C", GpuTempYellow, GpuTempRed);
        UpdateTextBlock(_gpuVramTemperature, gpuVramTemp, "{0:F0}°C", GpuTempYellow, GpuTempRed);
        UpdateTextBlock(_gpuPower, gpuPower, "{0:F1}W");
        UpdateTextBlock(_gpuFanSpeed, gpuFanSpeed);

        // Memory & PCH
        UpdateTextBlock(_memUsage, memUsage, "{0:F0}%", MemUsageYellow, MemUsageRed);
        UpdateTextBlock(_memTemperature, memTemp, "{0:F0}°C", MemTempYellow, MemTempRed);
        UpdateTextBlock(_pchTemperature, pchTemp, "{0:F0}°C", PchTempYellow, PchTempRed);
        UpdateTextBlock(_pchFanSpeed, pchFanSpeed);
    }

    private static Brush SeverityBrush(double value, double yellowThreshold, double redThreshold)
    {
        if (double.IsNaN(value)) return Brushes.White;
        if (value >= redThreshold) return Brushes.Red;
        if (value >= yellowThreshold) return Brushes.Goldenrod;
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

        _fpsController.FpsDataUpdated += OnFpsDataUpdated;
    }

    private async Task StartFpsMonitoringAsync()
    {
        await _fpsController.StartMonitoringAsync();
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

        int.TryParse(fps?.Trim(), out var fpsVal);
        int.TryParse(lowFps?.Trim(), out var lowVal);
        double.TryParse(frameTime?.Trim(), out var ftVal);

        var fpsText = (fpsVal >= 0) ? fpsVal.ToString() : dash;
        var lowFpsText = (lowVal >= 0) ? lowVal.ToString() : dash;
        var frameTimeText = (ftVal >= 0) ? $"{ftVal:F1}ms" : dash;

        SetTextIfChanged(_fps, fpsText);
        SetTextIfChanged(_lowFps, lowFpsText);
        SetTextIfChanged(_frameTime, frameTimeText);

        var normalBrush = Brushes.White;
        var alertBrush = Brushes.Red;

        var fpsBrush = (fpsVal >= 0 && fpsVal < FpsRedLine) ? alertBrush : normalBrush;
        var lowFpsBrush = (lowVal >= 0 && fpsVal >= 0 && (fpsVal - lowVal) >= 30) ? alertBrush : normalBrush;
        var frameTimeBrush = (ftVal >= 0 && ftVal > MaxFrameTimeMs) ? alertBrush : normalBrush;

        SetForegroundIfChanged(_fps, fpsBrush);
        SetForegroundIfChanged(_lowFps, lowFpsBrush);
        SetForegroundIfChanged(_frameTime, frameTimeBrush);
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
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    if (Log.Instance.IsTraceEnabled)
                    {
                        Log.Instance.Trace($"Exception occurred when executing TheRing()", ex);
                    }
                    await Task.Delay(1000, token);
                }
            }
        }
        finally
        {
            try
            {
                _refreshLock.Release();
            }
            catch (ObjectDisposedException) {}
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

        if ((DateTime.Now - _lastUpdate).TotalMilliseconds < UI_UPDATE_THROTTLE_MS) return;
        _lastUpdate = DateTime.Now;

        var data = dataTask.Result;

        await Dispatcher.BeginInvoke(() => UpdateSensorData(
            data.CPU.Utilization,
            data.CPU.CoreClock,
            data.CPU.Temperature,
            cpuPowerTask.Result,
            data.GPU.Utilization,
            data.GPU.CoreClock,
            data.GPU.Temperature,
            gpuVramTask.Result,
            gpuPowerTask.Result,
            memoryUsageTask.Result,
            data.PCH.Temperature,
            memoryTemperaturesTask.Result,
            data.CPU.FanSpeed,
            data.GPU.FanSpeed,
            data.PCH.FanSpeed
        ), DispatcherPriority.Normal);
    }
}