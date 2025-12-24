using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Appearance;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public partial class StatusWindow
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    private DateTime _lastUpdate = DateTime.MinValue;
    private const int UI_UPDATE_THROTTLE_MS = 100;
    private const int MAX_RETRY_COUNT = 3;
    private const int RETRY_DELAY_MS = 1000;
    private int _currentRetryCount;

    private readonly PowerModeFeature _powerModeFeature = IoCContainer.Resolve<PowerModeFeature>();
    private readonly ITSModeFeature _itsModeFeature = IoCContainer.Resolve<ITSModeFeature>();
    private readonly GodModeController _godModeController = IoCContainer.Resolve<GodModeController>();
    private readonly GPUController _gpuController = IoCContainer.Resolve<GPUController>();
    private readonly BatteryFeature _batteryFeature = IoCContainer.Resolve<BatteryFeature>();
    private readonly UpdateChecker _updateChecker = IoCContainer.Resolve<UpdateChecker>();
    private readonly UpdateCheckSettings _updateCheckerSettings = IoCContainer.Resolve<UpdateCheckSettings>();
    private readonly SensorsController _sensorsController = IoCContainer.Resolve<SensorsController>();
    private readonly SensorsGroupController _sensorsGroupController = IoCContainer.Resolve<SensorsGroupController>();

    private readonly struct StatusWindowData(
        PowerModeState? powerModeState,
        ITSMode? itsMode,
        string? godModePresetName,
        GPUStatus? gpuStatus,
        BatteryInformation? batteryInformation,
        BatteryState? batteryState,
        bool hasUpdate,
        SensorsData? sensorData = null,
        double cpuPower = -1,
        double gpuPower = -1)
    {
        public PowerModeState? PowerModeState { get; } = powerModeState;
        public ITSMode? ITSMode { get; } = itsMode;
        public string? GodModePresetName { get; } = godModePresetName;
        public GPUStatus? GPUStatus { get; } = gpuStatus;
        public BatteryInformation? BatteryInformation { get; } = batteryInformation;
        public BatteryState? BatteryState { get; } = batteryState;
        public bool HasUpdate { get; } = hasUpdate;
        public SensorsData? SensorsData { get; } = sensorData;
        public double CpuPower { get; } = cpuPower;
        public double GpuPower { get; } = gpuPower;
    }

    public static Task<StatusWindow> CreateAsync() => Task.FromResult(new StatusWindow());

    public StatusWindow()
    {
        InitializeComponent();

        Loaded += StatusWindow_Loaded;
        IsVisibleChanged += StatusWindow_IsVisibleChanged;

        WindowStyle = WindowStyle.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowBackdropType = BackgroundType.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;

        Focusable = false;
        Topmost = true;
        ExtendsContentIntoTitleBar = true;
        ShowInTaskbar = false;
        ShowActivated = false;

#if DEBUG
        _title.Text += " [DEBUG]";
#else
        var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
        if (version == new Version(0, 0, 1, 0) || version?.Build == 99)
        {
            _title.Text += " [BETA]";
        }
#endif

        if (Log.Instance.IsTraceEnabled)
        {
            _title.Text += " [LOGGING ENABLED]";
        }

        ConfigureDashboardVisibility();
    }

    private void ConfigureDashboardVisibility()
    {
        var mi = Compatibility.GetMachineInformationAsync().Result;
        var useNew = _settings.Store.UseNewSensorDashboard;
        var isSupportedSeries = (int)mi.LegionSeries <= 7;
        var sensorVisibility = (useNew && isSupportedSeries) ? Visibility.Visible : Visibility.Collapsed;

        _cpuGrid.Visibility = sensorVisibility;

        _cpuFreqAndTempDesc.Visibility = sensorVisibility;
        _cpuFreqAndTempLabel.Visibility = sensorVisibility;
        _cpuFanAndPowerDesc.Visibility = sensorVisibility;
        _cpuFanAndPowerLabel.Visibility = sensorVisibility;

        _systemFanGrid.Visibility = sensorVisibility;

        _gpuFreqAndTempDesc.Visibility = sensorVisibility;
        _gpuFreqAndTempLabel.Visibility = sensorVisibility;
        _gpuFanAndPowerDesc.Visibility = sensorVisibility;
        _gpuFanAndPowerLabel.Visibility = sensorVisibility;

        if (_sensorsController.GetType() == typeof(SensorsControllerV4) && _sensorsController.GetType() == typeof(SensorsControllerV5))
        {
            return;
        }

        _systemFanGrid.Visibility = Visibility.Collapsed;
        _systemFanLabel.Visibility = Visibility.Collapsed;
    }


    private void StatusWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible)
        {
            _cancellationTokenSource.Cancel();
        }
    }

    private async void StatusWindow_Loaded(object sender, RoutedEventArgs e)
    {
        MoveBottomRightEdgeOfWindowToMousePosition();

        var token = _cancellationTokenSource.Token;

        if (_settings.Store.UseNewSensorDashboard)
        {
            _ = Task.Run(async () => await TheRing(token), token);
        }
        else
        {
            try
            {
                var data = await GetStatusWindowDataAsync(token);
                ApplyDataToUI(data);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Error during initial data fetch: {ex.Message}", ex);
            }
        }
    }

    private async Task<StatusWindowData> GetStatusWindowDataAsync(CancellationToken token)
    {
        PowerModeState? state = null;
        ITSMode? mode = null;
        string? godModePresetName = null;
        GPUStatus? gpuStatus = null;
        BatteryInformation? batteryInformation = null;
        BatteryState? batteryState = null;
        var hasUpdate = false;

        SensorsData? sensorsData = null;
        double cpuPower = -1;
        double gpuPower = -1;

        try
        {
            if (await _powerModeFeature.IsSupportedAsync().WaitAsync(token))
            {
                state = await _powerModeFeature.GetStateAsync().WaitAsync(token);
                if (state == PowerModeState.GodMode)
                    godModePresetName = await _godModeController.GetActivePresetNameAsync().WaitAsync(token);
            }
            if (await _itsModeFeature.IsSupportedAsync().WaitAsync(token))
                mode = await _itsModeFeature.GetStateAsync().WaitAsync(token);
        }
        catch { /* Ignore */ }

        try
        {
            if (_gpuController.IsSupported())
                gpuStatus = await _gpuController.RefreshNowAsync().WaitAsync(token);
        }
        catch { /* Ignore */ }

        try { batteryInformation = Battery.GetBatteryInformation(); } catch { }

        try
        {
            if (await _batteryFeature.IsSupportedAsync().WaitAsync(token))
                batteryState = await _batteryFeature.GetStateAsync().WaitAsync(token);
        }
        catch { /* Ignore */ }

        try
        {
            if (_updateCheckerSettings.Store.UpdateCheckFrequency != UpdateCheckFrequency.Never)
                hasUpdate = await _updateChecker.CheckAsync(false).WaitAsync(token) is not null;
        }
        catch { /* Ignore */ }

        if (!_settings.Store.UseNewSensorDashboard)
        {
            return new(state, mode, godModePresetName, gpuStatus, batteryInformation, batteryState, hasUpdate, sensorsData, cpuPower, gpuPower);
        }

        try
        {
            if (await _sensorsController.IsSupportedAsync().WaitAsync(token))
                sensorsData = await _sensorsController.GetDataAsync().WaitAsync(token);

            var states = await _sensorsGroupController.IsSupportedAsync().WaitAsync(token);
            if (states is LibreHardwareMonitorInitialState.Success or LibreHardwareMonitorInitialState.Initialized)
            {
                await _sensorsGroupController.UpdateAsync().WaitAsync(token);
                for (int i = 0; i < MAX_RETRY_COUNT; i++)
                {
                    cpuPower = await _sensorsGroupController.GetCpuPowerAsync().WaitAsync(token);
                    gpuPower = await _sensorsGroupController.GetGpuPowerAsync().WaitAsync(token);

                    if (cpuPower > 0 && (gpuStatus?.State != GPUState.Active || gpuPower > 0))
                        break;

                    await Task.Delay(RETRY_DELAY_MS, token);
                    await _sensorsGroupController.UpdateAsync().WaitAsync(token);
                }
            }
        }
        catch (Exception ex) { Log.Instance.Trace($"Sensor error: {ex.Message}"); }

        return new(state, mode, godModePresetName, gpuStatus, batteryInformation, batteryState, hasUpdate, sensorsData, cpuPower, gpuPower);
    }

    private async Task TheRing(CancellationToken token)
    {
        if (!await _refreshLock.WaitAsync(0, token)) return;

        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var data = await GetStatusWindowDataAsync(token);
                    token.ThrowIfCancellationRequested();

                    if ((DateTime.Now - _lastUpdate).TotalMilliseconds >= UI_UPDATE_THROTTLE_MS)
                    {
                        _lastUpdate = DateTime.Now;
                        await Dispatcher.InvokeAsync(() => ApplyDataToUI(data), System.Windows.Threading.DispatcherPriority.Normal, token);
                    }

                    _currentRetryCount = 0;
                    await Task.Delay(TimeSpan.FromSeconds(_settings.Store.FloatingGadgetsRefreshInterval), token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Exception in loop: {ex.Message}", ex);
                    if (++_currentRetryCount < MAX_RETRY_COUNT)
                    {
                        await Task.Delay(RETRY_DELAY_MS, token);
                        continue;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(_settings.Store.FloatingGadgetsRefreshInterval), token);
                }
            }
        }
        finally
        {
            try { _refreshLock.Release(); } catch { }
        }
    }

    private void ApplyDataToUI(StatusWindowData data)
    {
        RefreshPowerMode(data.PowerModeState, data.ITSMode, data.GodModePresetName);

        if (_settings.Store.UseNewSensorDashboard)
        {
            UpdateFreqAndTemp(_cpuFreqAndTempLabel, data.SensorsData?.CPU.CoreClock ?? -1, data.SensorsData?.CPU.Temperature ?? -1);
            UpdateFanAndPower(_cpuFanAndPowerLabel, data.SensorsData?.CPU.FanSpeed ?? -1, data.CpuPower);
            UpdateSystemFan(_systemFanLabel, data.SensorsData?.PCH.FanSpeed ?? -1);
        }

        RefreshDiscreteGpu(data.GPUStatus, data.SensorsData, data.GpuPower);

        RefreshBattery(data.BatteryInformation, data.BatteryState);
        RefreshUpdate(data.HasUpdate);
    }

    private void RefreshPowerMode(PowerModeState? powerModeState, ITSMode? itsMode, string? godModePresetName)
    {
        if (powerModeState != null)
        {
            _powerModeValueLabel.Content = powerModeState.GetDisplayName();
            _powerModeValueIndicator.Fill = powerModeState?.GetSolidColorBrush() ?? new(Colors.Transparent);
            bool isGodMode = powerModeState == PowerModeState.GodMode;
            _powerModePresetLabel.Visibility = isGodMode ? Visibility.Visible : Visibility.Collapsed;
            _powerModePresetValueLabel.Visibility = isGodMode ? Visibility.Visible : Visibility.Collapsed;
            _powerModePresetValueLabel.Content = godModePresetName ?? "-";
        }
        else
        {
            _powerModeValueLabel.Content = itsMode?.GetDisplayName() ?? "-";
            _powerModeValueIndicator.Fill = itsMode?.GetSolidColorBrush() ?? Brushes.Transparent;
            _powerModePresetLabel.Visibility = Visibility.Collapsed;
            _powerModePresetValueLabel.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshDiscreteGpu(GPUStatus? status, SensorsData? sensorsData, double gpuPower)
    {
        if (!status.HasValue)
        {
            _gpuGrid.Visibility = Visibility.Collapsed;
            return;
        }

        var state = status.Value.State;
        _gpuActive.Visibility = state is GPUState.Active or GPUState.MonitorConnected ? Visibility.Visible : Visibility.Collapsed;
        _gpuInactive.Visibility = state == GPUState.Inactive ? Visibility.Visible : Visibility.Collapsed;
        _gpuPoweredOff.Visibility = state == GPUState.PoweredOff ? Visibility.Visible : Visibility.Collapsed;

        var detailsVisible = state != GPUState.PoweredOff;
        _gpuPowerStateValue.Visibility = detailsVisible ? Visibility.Visible : Visibility.Collapsed;
        _gpuPowerStateValueLabel.Visibility = detailsVisible ? Visibility.Visible : Visibility.Collapsed;
        _gpuPowerStateValueLabel.Content = status.Value.PerformanceState ?? "-";

        if (_settings.Store.UseNewSensorDashboard && detailsVisible)
        {
            _gpuFreqAndTempLabel.Visibility = Visibility.Visible;
            _gpuFanAndPowerLabel.Visibility = Visibility.Visible;
            UpdateFreqAndTemp(_gpuFreqAndTempLabel, sensorsData?.GPU.CoreClock ?? -1, sensorsData?.GPU.Temperature ?? -1);
            UpdateFanAndPower(_gpuFanAndPowerLabel, sensorsData?.GPU.FanSpeed ?? -1, gpuPower);
        }
        else
        {

            if (_gpuFreqAndTempLabel != null) _gpuFreqAndTempLabel.Visibility = Visibility.Collapsed;
            if (_gpuFanAndPowerLabel != null) _gpuFanAndPowerLabel.Visibility = Visibility.Collapsed;
        }

        _gpuGrid.Visibility = Visibility.Visible;
    }

    private void RefreshBattery(BatteryInformation? batteryInformation, BatteryState? batteryState)
    {
        if (!batteryInformation.HasValue || !batteryState.HasValue)
        {
            _batteryIcon.Symbol = SymbolRegular.Battery024;
            _batteryValueLabel.Content = "-";
            return;
        }

        var info = batteryInformation.Value;
        _batteryIcon.Symbol = (int)Math.Round(info.BatteryPercentage / 10.0) switch
        {
            10 => SymbolRegular.Battery1024,
            9 => SymbolRegular.Battery924,
            8 => SymbolRegular.Battery824,
            7 => SymbolRegular.Battery724,
            6 => SymbolRegular.Battery624,
            5 => SymbolRegular.Battery524,
            4 => SymbolRegular.Battery424,
            3 => SymbolRegular.Battery324,
            2 => SymbolRegular.Battery224,
            1 => SymbolRegular.Battery124,
            _ => SymbolRegular.Battery024
        };

        if (info.IsCharging)
            _batteryIcon.Symbol = batteryState == BatteryState.Conservation ? SymbolRegular.BatterySaver24 : SymbolRegular.BatteryCharge24;

        if (info.IsLowBattery) _batteryValueLabel.SetResourceReference(ForegroundProperty, "SystemFillColorCautionBrush");
        else _batteryValueLabel.ClearValue(ForegroundProperty);

        _batteryValueLabel.Content = $"{info.BatteryPercentage}%";
        _batteryModeValueLabel.Content = batteryState.GetDisplayName();
        _batteryDischargeValueLabel.Content = $"{info.DischargeRate / 1000.0:+0.00;-0.00;0.00} W";
        _batteryMinDischargeValueLabel.Content = $"{info.MinDischargeRate / 1000.0:+0.00;-0.00;0.00} W";
        _batteryMaxDischargeValueLabel.Content = $"{info.MaxDischargeRate / 1000.0:+0.00;-0.00;0.00} W";
    }

    private void RefreshUpdate(bool hasUpdate) => _updateIndicator.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;

    private static string GetTemperatureText(double temperature)
    {
        var settings = IoCContainer.Resolve<ApplicationSettings>();
        if (temperature <= 0) return "-";
        return settings.Store.TemperatureUnit == TemperatureUnit.F ? $"{(temperature * 9 / 5 + 32):0}{Resource.Fahrenheit}" : $"{temperature:0}{Resource.Celsius}";
    }

    private static void UpdateFreqAndTemp(System.Windows.Controls.Label label, double freq, double temp) =>
        label.Content = (temp < 0 || freq < 0) ? "-" : $"{freq:0}Mhz | {GetTemperatureText(temp)}";

    private static void UpdateFanAndPower(System.Windows.Controls.Label label, double fan, double power) =>
        label.Content = (fan < 0 || power < 0) ? "-" : $"{fan:0}RPM | {power:0}W";

    private static void UpdateSystemFan(System.Windows.Controls.Label label, double fan) =>
        label.Content = fan < 0 ? "-" : $"{fan:0}RPM";

    private void MoveBottomRightEdgeOfWindowToMousePosition()
    {
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
        if (!transform.HasValue)
        {
            Left = 0;
            Top = 0;
            return;
        }

        const double offset = 8;

        var mousePoint = Control.MousePosition;
        var screenRectangle = Screen.FromPoint(mousePoint).WorkingArea;

        var mouse = transform.Value.Transform(new Point(mousePoint.X, mousePoint.Y));
        var screen = transform.Value.Transform(new Vector(screenRectangle.Width, screenRectangle.Height));

        if (mouse.X + offset + ActualWidth > screen.X)
            Left = mouse.X - ActualWidth - offset;
        else
            Left = mouse.X + offset;

        if (mouse.Y + offset + ActualHeight > screen.Y)
            Top = mouse.Y - ActualHeight - offset;
        else
            Top = mouse.Y + offset;
    }
}