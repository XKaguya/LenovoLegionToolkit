using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Settings;
using WpfScreenHelper;

namespace LenovoLegionToolkit.WPF.Windows.Osd;

public abstract class OsdWindowBase : Window
{
    #region Win32

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    #endregion

    #region Threshold Constants

    protected int _uiUpdateThrottleMs = 0;
    protected const double MAX_FRAME_TIME_MS = 10.0;
    protected const long FRAMETIME_TIMEOUT_TICKS = 2 * 10_000_000;

    protected Brush? _labelBrush;
    protected Brush _warningBrush = Brushes.Goldenrod;
    protected Brush _criticalBrush = Brushes.Red;

    #endregion

    #region Services

    protected readonly OsdSettings _OsdSettings = IoCContainer.Resolve<OsdSettings>();
    protected readonly SensorsController _controller = IoCContainer.Resolve<SensorsController>();
    protected readonly SensorsGroupController _sensorsGroupControllers = IoCContainer.Resolve<SensorsGroupController>();
    protected readonly FpsSensorController _fpsController = IoCContainer.Resolve<FpsSensorController>();
    protected readonly SensorsControlSettings _sensorsControlSettings = IoCContainer.Resolve<SensorsControlSettings>();

    #endregion

    #region State

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    protected readonly StringBuilder _stringBuilder = new(64);

    protected DateTime _lastUpdate = DateTime.MinValue;
    private long _lastValidFpsTick;
    private long _lastFpsUiUpdateTick;

    private CancellationTokenSource? _cts;
    protected bool _positionSet;
    private bool _fpsMonitoringStarted;

    protected HashSet<OsdItem> _activeItems = [];
    protected Dictionary<OsdItem, FrameworkElement> _itemsMap = [];
    protected Dictionary<FrameworkElement, (List<OsdItem> Items, FrameworkElement? Separator)> _measurementGroups = [];

    #endregion

    #region Initialization

    protected void InitOsd()
    {
        _activeItems = new HashSet<OsdItem>(_OsdSettings.Store.Items);

        IsVisibleChanged += OnVisibilityChanged;
        SourceInitialized += OnSourceInitialized;
        Closed += OnWindowClosed;
        Loaded += OnLoaded;
        ContentRendered += OnContentRendered;
        LocationChanged += OnLocationChanged;
        MouseLeftButtonDown += OnMouseLeftButtonDown;

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        InitializeComponentSpecifics();
        SubscribeEvents();
        _fpsController.FpsDataUpdated += OnFpsDataUpdated;

        ApplyAppearanceSettings();
        UpdateMeasurementControlsVisibility();
    }

    private async void InitializeComponentSpecifics()
    {
        var mi = await Compatibility.GetMachineInformationAsync();
        if (mi.Properties.IsAmdDevice)
        {
            OnAmdDeviceDetected();
        }
    }

    protected abstract void OnAmdDeviceDetected();

    private void SubscribeEvents()
    {
        MessagingCenter.Subscribe<OsdElementChangedMessage>(this, (message) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (App.Current.OsdWindow == null) return;

                var newItemsSet = new HashSet<OsdItem>(message.Items);
                if (_activeItems.SetEquals(newItemsSet)) return;

                _activeItems = newItemsSet;
                UpdateMeasurementControlsVisibility();
            });
        });

        MessagingCenter.Subscribe<OsdAppearanceChangedMessage>(this, _ =>
        {
            Dispatcher.Invoke(ApplyAppearanceSettings);
        });
    }

    #endregion

    #region Window Events

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        UpdateClickThrough();
    }

    private void UpdateClickThrough()
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source) return;

        var hwnd = source.Handle;
        var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

        extendedStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;

        if (_OsdSettings.Store.IsLocked)
            extendedStyle |= WS_EX_TRANSPARENT;
        else
            extendedStyle &= ~WS_EX_TRANSPARENT;

        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_OsdSettings.Store.IsLocked && e.ChangedButton == MouseButton.Left)
        {
            DragMove();

            var screen = WpfScreenHelper.Screen.FromWindow(this);
            if (screen != null)
            {
                var workArea = screen.WpfWorkingArea;
                
                double snapThreshold = 32.0;

                double left = this.Left;
                double top = this.Top;
                double width = this.ActualWidth > 0 ? this.ActualWidth : this.Width;
                double height = this.ActualHeight > 0 ? this.ActualHeight : this.Height;

                if (Math.Abs(left - workArea.Left) < snapThreshold)
                {
                    left = workArea.Left;
                }
                else if (Math.Abs(workArea.Right - (left + width)) < snapThreshold)
                {
                    left = workArea.Right - width;
                }

                if (Math.Abs(top - workArea.Top) < snapThreshold)
                {
                    top = workArea.Top;
                }
                else if (Math.Abs(workArea.Bottom - (top + height)) < snapThreshold)
                {
                    top = workArea.Bottom - height;
                }
                
                if (left < workArea.Left) left = workArea.Left;
                if (left + width > workArea.Right) left = workArea.Right - width;
                
                if (top < workArea.Top) top = workArea.Top;
                if (top + height > workArea.Bottom) top = workArea.Bottom - height;

                this.Left = left;
                this.Top = top;
                
                _OsdSettings.SynchronizeStore();
            }
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
        => Dispatcher.BeginInvoke(new Action(SetWindowPosition), DispatcherPriority.Loaded);

    private void OnContentRendered(object? sender, EventArgs e)
    {
        if (!_positionSet)
            Dispatcher.BeginInvoke(new Action(SetWindowPosition), DispatcherPriority.Render);
    }

    protected virtual void SetWindowPosition()
    {
        if (SavedPositionX.HasValue && SavedPositionY.HasValue)
        {
            var savedX = SavedPositionX.Value;
            var savedY = SavedPositionY.Value;

            if (IsPositionOnScreen(savedX, savedY))
            {
                Left = savedX;
                Top = savedY;
                _positionSet = true;
                return;
            }

            SavedPositionX = null;
            SavedPositionY = null;
            _OsdSettings.SynchronizeStore();
        }

        SetDefaultWindowPosition();
    }

    protected abstract void SetDefaultWindowPosition();
    protected abstract double? SavedPositionX { get; set; }
    protected abstract double? SavedPositionY { get; set; }

    public void RecalculatePosition()
    {
        SavedPositionX = null;
        SavedPositionY = null;
        _OsdSettings.SynchronizeStore();
        
        SetDefaultWindowPosition();
    }

    private const int MONITOR_DEFAULTTONULL = 0;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, int dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private static bool IsPositionOnScreen(double x, double y)
    {
        var pt = new POINT { X = (int)x, Y = (int)y };
        return MonitorFromPoint(pt, MONITOR_DEFAULTTONULL) != IntPtr.Zero;
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!IsLoaded) return;

        SavedPositionX = Left;
        SavedPositionY = Top;
        _OsdSettings.SynchronizeStore();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsPositionOnScreen(Left, Top))
                SetDefaultWindowPosition();
        });
    }

    private async void OnVisibilityChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _sensorsGroupControllers.ShowAverageCpuFrequency = _sensorsControlSettings.Store.ShowCpuAverageFrequency;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            CheckAndUpdateFpsMonitoring();
            UpdateMeasurementControlsVisibility();

            await TheRing(_cts.Token);
        }
        else
        {
            _cts?.Cancel();
            CheckAndUpdateFpsMonitoring();
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        SavedPositionX = Left;
        SavedPositionY = Top;
        _OsdSettings.SynchronizeStore();

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

        _cts?.Cancel();
        _cts?.Dispose();
        _refreshLock.Dispose();

        _fpsController.FpsDataUpdated -= OnFpsDataUpdated;
        _fpsController.Dispose();

        App.Current.OsdWindow = null;
    }
    
    protected virtual void ApplyAppearanceSettings()
    {
        var converter = new BrushConverter();

        _labelBrush = _OsdSettings.Store.LabelColorSource == OsdColorSource.Custom && !string.IsNullOrEmpty(_OsdSettings.Store.LabelColor)
            ? (Brush)converter.ConvertFromString(_OsdSettings.Store.LabelColor)!
            : null;

        _warningBrush = (Brush)converter.ConvertFromString(_OsdSettings.Store.WarningColor)!;
        _criticalBrush = (Brush)converter.ConvertFromString(_OsdSettings.Store.CriticalColor)!;

        UpdateClickThrough();

        if (!SavedPositionX.HasValue || !SavedPositionY.HasValue)
        {
            SetDefaultWindowPosition();
        }
    }

    protected void ApplyCornerRadius(Border border)
    {
        if (border != null)
        {
            border.CornerRadius = new CornerRadius(
                _OsdSettings.Store.CornerRadiusTop,
                _OsdSettings.Store.CornerRadiusTop,
                _OsdSettings.Store.CornerRadiusBottom,
                _OsdSettings.Store.CornerRadiusBottom);
        }
    }

    #endregion

    #region Visibility

    protected void UpdateMeasurementControlsVisibility()
    {
        bool isHybrid = _sensorsGroupControllers.IsHybrid;

        foreach (var (item, element) in _itemsMap)
        {
            bool shouldShow = _activeItems.Contains(item);

            if (isHybrid)
            {
                if (item == OsdItem.CpuFrequency) shouldShow = false;
            }
            else
            {
                if (item is OsdItem.CpuPCoreFrequency or OsdItem.CpuECoreFrequency)
                    shouldShow = false;
            }

            element.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
            OnItemVisibilityChanged(element, shouldShow);
        }

        var visibleGroups = new List<FrameworkElement>();

        foreach (var (groupPanel, (items, _)) in _measurementGroups)
        {
            bool isGroupActive = items.Any(item =>
            {
                if (!_activeItems.Contains(item)) return false;
                if (isHybrid && item == OsdItem.CpuFrequency) return false;
                if (!isHybrid && item is OsdItem.CpuPCoreFrequency or OsdItem.CpuECoreFrequency) return false;
                return true;
            });

            groupPanel.Visibility = isGroupActive ? Visibility.Visible : Visibility.Collapsed;
            if (isGroupActive) visibleGroups.Add(groupPanel);
        }

        foreach (var (groupPanel, (_, separator)) in _measurementGroups)
        {
            if (separator == null) continue;

            int index = visibleGroups.IndexOf(groupPanel);
            separator.Visibility = (index >= 0 && index < visibleGroups.Count - 1)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        CheckAndUpdateFpsMonitoring();
    }

    protected virtual void OnItemVisibilityChanged(FrameworkElement element, bool visible) { }

    #endregion

    #region UI Helpers

    protected void UpdateTextBlock(TextBlock tb, double value, string format,
        double yellowThreshold = double.MaxValue, double redThreshold = double.MaxValue)
    {
        if (tb.Visibility != Visibility.Visible) return;

        string text;
        Brush foreground = Brushes.White;

        if (double.IsNaN(value) || value < 0)
        {
            text = "-";
        }
        else
        {
            _stringBuilder.Clear();
            _stringBuilder.AppendFormat(format, value);
            text = _stringBuilder.ToString();

            if (yellowThreshold != double.MaxValue)
                foreground = SeverityBrush(value, yellowThreshold, redThreshold);
        }

        SetTextIfChanged(tb, text);
        SetForegroundIfChanged(tb, foreground);
    }

    protected void UpdateTextBlock(TextBlock tb, int value, string suffix = " RPM")
    {
        if (tb.Visibility != Visibility.Visible) return;
        SetTextIfChanged(tb, value < 0 ? "-" : $"{value}{suffix}");
    }

    protected Brush SeverityBrush(double value, double yellowThreshold, double redThreshold)
    {
        if (value >= redThreshold) return _criticalBrush;
        return value >= yellowThreshold ? _warningBrush : Brushes.White;
    }

    protected static void SetTextIfChanged(TextBlock tb, string text)
    {
        if (!string.Equals(tb.Text, text, StringComparison.Ordinal))
            tb.Text = text;
    }

    protected static void SetForegroundIfChanged(TextBlock tb, Brush brush)
    {
        if (!ReferenceEquals(tb.Foreground, brush))
            tb.Foreground = brush;
    }

    #endregion

    #region FPS Monitoring

    private async void CheckAndUpdateFpsMonitoring()
    {
        bool shouldMonitor = IsVisible && ShouldMonitorFps();

        switch (shouldMonitor)
        {
            case true when !_fpsMonitoringStarted:
                await StartFpsMonitoringAsync();
                _fpsMonitoringStarted = true;
                break;
            case false when _fpsMonitoringStarted:
                StopFpsMonitoring();
                _fpsMonitoringStarted = false;
                break;
        }
    }

    private bool ShouldMonitorFps() =>
        _activeItems.Contains(OsdItem.Fps) ||
        _activeItems.Contains(OsdItem.LowFps) ||
        _activeItems.Contains(OsdItem.FrameTime);

    private async Task StartFpsMonitoringAsync()
    {
        try { await _fpsController.StartMonitoringAsync(); }
        catch (Exception ex) { Log.Instance.Trace($"Failed to start FPS monitoring", ex); }
    }

    private void StopFpsMonitoring()
    {
        try { _fpsController.StopMonitoring(); }
        catch (Exception ex) { Log.Instance.Trace($"Failed to stop FPS monitoring", ex); }
    }

    private void OnFpsDataUpdated(object? sender, FpsSensorController.FpsData fpsData)
    {
        if (!_fpsMonitoringStarted) return;
        if (string.IsNullOrWhiteSpace(fpsData.Fps)) return;

        long currentTick = DateTime.Now.Ticks;

        int.TryParse(fpsData.Fps?.Trim(), out var fpsVal);
        int.TryParse(fpsData.LowFps?.Trim(), out var lowVal);
        double.TryParse(fpsData.FrameTime?.Trim(), out var ftVal);

        bool isSampleValid = fpsVal > 0;

        string? fpsText = null, lowText = null, ftText = null;
        Brush? fpsBrush = null, lowBrush = null, ftBrush = null;

        if (isSampleValid)
        {
            long elapsedTicks = currentTick - _lastFpsUiUpdateTick;
            if (_uiUpdateThrottleMs > 0 && elapsedTicks < TimeSpan.FromMilliseconds(_uiUpdateThrottleMs).Ticks) return;

            _lastFpsUiUpdateTick = currentTick;
            _lastValidFpsTick = currentTick;

            const string dash = "-";

            fpsText = fpsVal.ToString();
            fpsBrush = (fpsVal < _OsdSettings.Store.FpsThresholdRed) ? _criticalBrush : Brushes.White;

            lowText = (lowVal > 0) ? lowVal.ToString() : dash;
            lowBrush = (lowVal > 0 && (fpsVal - lowVal) >= _OsdSettings.Store.LowFpsDeltaThreshold) ? _criticalBrush : Brushes.White;

            if (ftVal > 0.1)
            {
                ftText = $"{ftVal,5:F1}ms";
                ftBrush = (ftVal > MAX_FRAME_TIME_MS) ? _criticalBrush : Brushes.White;
            }
            else
            {
                ftText = dash;
                ftBrush = Brushes.White;
            }
        }
        else
        {
            if (currentTick - _lastValidFpsTick > FRAMETIME_TIMEOUT_TICKS)
            {
                const string dash = "-";
                fpsText = dash; fpsBrush = Brushes.White;
                lowText = dash; lowBrush = Brushes.White;
                ftText = dash; ftBrush = Brushes.White;
                _lastFpsUiUpdateTick = currentTick;
            }
            else
            {
                return;
            }
        }

        var displayData = new FpsDisplayData
        {
            FpsText = fpsText,
            FpsBrush = fpsBrush,
            LowFpsText = lowText,
            LowFpsBrush = lowBrush,
            FrameTimeText = ftText,
            FrameTimeBrush = ftBrush
        };

        Dispatcher.BeginInvoke(() => UpdateFpsDisplay(displayData), DispatcherPriority.Normal);
    }

    protected abstract void UpdateFpsDisplay(FpsDisplayData data);

    #endregion

    #region Main Loop & Data Refresh

    private async Task TheRing(CancellationToken token)
    {
        if (!await _refreshLock.WaitAsync(0, token)) return;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var loopStart = DateTime.Now;
                try
                {
                    await RefreshSensorsDataAsync(token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Exception occurred when executing TheRing()", ex);
                    await Task.Delay(1000, token);
                }

                var elapsed = DateTime.Now - loopStart;
                var delay = TimeSpan.FromSeconds(_OsdSettings.Store.OsdRefreshInterval) - elapsed;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, token);
                }
            }
        }
        finally
        {
            try { _refreshLock.Release(); } catch (ObjectDisposedException) { }
        }
    }

    private async Task RefreshSensorsDataAsync(CancellationToken token)
    {
        await _sensorsGroupControllers.UpdateAsync();

        var dataTask = _controller.GetDataAsync();
        var cpuPowerTask = _sensorsGroupControllers.GetCpuPowerAsync();
        var gpuPowerTask = _sensorsGroupControllers.GetGpuPowerAsync();
        var gpuVramTask = _sensorsGroupControllers.GetGpuVramTemperatureAsync();
        var memUsageTask = _sensorsGroupControllers.GetMemoryUsageAsync();
        var memTempTask = _sensorsGroupControllers.GetHighestMemoryTemperatureAsync();
        var diskTempsTask = _sensorsGroupControllers.GetSsdTemperaturesAsync();

        var cpuClockTask = !_sensorsGroupControllers.IsHybrid ? _sensorsGroupControllers.GetCpuCoreClockAsync() : Task.FromResult(float.NaN);
        var cpuPClockTask = _sensorsGroupControllers.IsHybrid ? _sensorsGroupControllers.GetCpuPCoreClockAsync() : Task.FromResult(float.NaN);
        var cpuEClockTask = _sensorsGroupControllers.IsHybrid ? _sensorsGroupControllers.GetCpuECoreClockAsync() : Task.FromResult(float.NaN);

        await Task.WhenAll(dataTask, cpuPowerTask, gpuPowerTask, gpuVramTask, memUsageTask, memTempTask, diskTempsTask, cpuPClockTask, cpuEClockTask);

        if (token.IsCancellationRequested) return;

        if (_uiUpdateThrottleMs > 0 && (DateTime.Now - _lastUpdate).TotalMilliseconds < _uiUpdateThrottleMs) return;

        _lastUpdate = DateTime.Now;

        var mainData = await dataTask;
        var diskData = await diskTempsTask;

        var snapshot = new SensorSnapshot
        {
            CpuUsage = mainData.CPU.Utilization,
            CpuFrequency = await cpuClockTask,
            CpuPClock = await cpuPClockTask,
            CpuEClock = await cpuEClockTask,
            CpuTemp = mainData.CPU.Temperature,
            CpuPower = await cpuPowerTask,
            CpuFanSpeed = mainData.CPU.FanSpeed,

            GpuUsage = mainData.GPU.Utilization,
            GpuFrequency = mainData.GPU.CoreClock,
            GpuTemp = mainData.GPU.Temperature,
            GpuVramTemp = await gpuVramTask,
            GpuPower = await gpuPowerTask,
            GpuFanSpeed = mainData.GPU.FanSpeed,

            MemUsage = await memUsageTask,
            MemTemp = await memTempTask,

            PchTemp = mainData.PCH.Temperature,
            PchFanSpeed = mainData.PCH.FanSpeed,

            Disk1Temp = diskData.Item1,
            Disk2Temp = diskData.Item2
        };

        await Dispatcher.BeginInvoke(() => UpdateSensorData(snapshot), DispatcherPriority.Normal);
    }

    protected abstract void UpdateSensorData(SensorSnapshot data);

    #endregion
}
