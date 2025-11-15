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
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public partial class StatusWindow
{
    private readonly CancellationTokenSource _cancellationTokenSource;

    private readonly PowerModeFeature _powerModeFeature;
    private readonly ITSModeFeature _itsModeFeature;
    private readonly GodModeController _godModeController;
    private readonly GPUController _gpuController;
    private readonly BatteryFeature _batteryFeature;
    private readonly UpdateChecker _updateChecker;
    private readonly UpdateCheckSettings _updateCheckerSettings;
    private readonly SensorsController _sensorsController;
    private readonly SensorsGroupController _sensorsGroupController;

    private readonly struct StatusWindowData(
        PowerModeState? powerModeState,
        ITSMode? itsMode,
        string? godModePresetName,
        GPUStatus? gpuStatus,
        BatteryInformation? batteryInformation,
        BatteryState? batteryState,
        bool hasUpdate,
        SensorsData? sensorData,
        double cpuPower,
        double gpuPower)
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

    public static Task<StatusWindow> CreateAsync()
    {
        return Task.FromResult(new StatusWindow());
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
        double cpuPower = 0;
        double gpuPower = 0;

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
        catch { /* Ignored */ }

        try
        {
            if (_gpuController.IsSupported())
                gpuStatus = await _gpuController.RefreshNowAsync().WaitAsync(token);
        }
        catch { /* Ignored */ }

        try
        {
            batteryInformation = Battery.GetBatteryInformation();
        }
        catch { /* Ignored */ }

        try
        {
            if (await _batteryFeature.IsSupportedAsync().WaitAsync(token))
                batteryState = await _batteryFeature.GetStateAsync().WaitAsync(token);
        }
        catch { /* Ignored */ }

        try
        {
            if (_updateCheckerSettings.Store.UpdateCheckFrequency != UpdateCheckFrequency.Never)
                hasUpdate = await _updateChecker.CheckAsync(false).WaitAsync(token) is not null;
        }
        catch { /* Ignored */ }

        try
        {
            if (await _sensorsController.IsSupportedAsync().WaitAsync(token))
                sensorsData = await _sensorsController.GetDataAsync().WaitAsync(token);
        }
        catch { /* Ignored */ }

        try
        {
            var states = await _sensorsGroupController.IsSupportedAsync().WaitAsync(token);
            if (states is LibreHardwareMonitorInitialState.Success or LibreHardwareMonitorInitialState.Initialized)
            {
                cpuPower = await _sensorsGroupController.GetCpuPowerAsync().WaitAsync(token);
                gpuPower = await _sensorsGroupController.GetGpuPowerAsync().WaitAsync(token);
            }
        }
        catch { /* Ignored */ }

        return new(state, mode, godModePresetName, gpuStatus, batteryInformation, batteryState, hasUpdate, sensorsData, cpuPower, gpuPower);
    }

    public StatusWindow()
    {
        InitializeComponent();

        _cancellationTokenSource = new CancellationTokenSource();

        _powerModeFeature = IoCContainer.Resolve<PowerModeFeature>();
        _itsModeFeature = IoCContainer.Resolve<ITSModeFeature>();
        _godModeController = IoCContainer.Resolve<GodModeController>();
        _gpuController = IoCContainer.Resolve<GPUController>();
        _batteryFeature = IoCContainer.Resolve<BatteryFeature>();
        _updateChecker = IoCContainer.Resolve<UpdateChecker>();
        _updateCheckerSettings = IoCContainer.Resolve<UpdateCheckSettings>();
        _sensorsController = IoCContainer.Resolve<SensorsController>();
        _sensorsGroupController = IoCContainer.Resolve<SensorsGroupController>();

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

        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.System && e.SystemKey == Key.LeftAlt)
            {
                e.Handled = true;
                Keyboard.ClearFocus();
            }
        };

#if DEBUG
        _title.Text += " [DEBUG]";
#else
        var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
        if (version == new Version(0, 0, 1, 0) || version?.Build == 99)
            _title.Text += " [BETA]";
#endif

        if (Log.Instance.IsTraceEnabled)
            _title.Text += " [LOGGING ENABLED]";
    }

    private void StatusWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible)
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
        }
    }

    private async void StatusWindow_Loaded(object sender, RoutedEventArgs e)
    {
        MoveBottomRightEdgeOfWindowToMousePosition();

        var token = _cancellationTokenSource.Token;
        await Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var data = await GetStatusWindowDataAsync(token);
                    if (token.IsCancellationRequested)
                        break;

                    await Dispatcher.InvokeAsync(() => TheRing(data), System.Windows.Threading.DispatcherPriority.Normal, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch { /* Ignored */ }

                try
                {
                    await Task.Delay(1000, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

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

        Left = mouse.X + offset + ActualWidth > screen.X
            ? mouse.X - ActualWidth - offset
            : mouse.X + offset;

        Top = mouse.Y + offset + ActualHeight > screen.Y
            ? mouse.Y - ActualHeight - offset
            : mouse.Y + offset;
    }

    private void TheRing(StatusWindowData data)
    {
        UpdateFreqAndTemp(_cpuFreqAndTempLabel, data.SensorsData?.CPU.CoreClock ?? -1, data.SensorsData?.CPU.Temperature ?? -1);
        UpdateFanAndPower(_cpuFanAndPowerLabel, data.SensorsData?.CPU.FanSpeed ?? -1, data.CpuPower);
        UpdateSystemFan(_systemFanLabel, data.SensorsData?.PCH.FanSpeed ?? -1);

        RefreshPowerMode(data.PowerModeState, data.ITSMode, data.GodModePresetName);
        RefreshDiscreteGpu(data.GPUStatus, data.SensorsData, data.GpuPower);
        RefreshBattery(data.BatteryInformation, data.BatteryState);
        RefreshUpdate(data.HasUpdate);
    }

    private void RefreshPowerMode(PowerModeState? powerModeState, ITSMode? itsMode, string? godModePresetName)
    {
        if (powerModeState != null)
        {
            _powerModeValueLabel.Content = powerModeState?.GetDisplayName() ?? "-";
            _powerModeValueIndicator.Fill = powerModeState?.GetSolidColorBrush() ?? Brushes.Transparent;

            var presetVisibility = powerModeState == PowerModeState.GodMode ? Visibility.Visible : Visibility.Collapsed;
            _powerModePresetValueLabel.Content = godModePresetName ?? "-";
            _powerModePresetLabel.Visibility = presetVisibility;
            _powerModePresetValueLabel.Visibility = presetVisibility;
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
        var performanceState = status.Value.PerformanceState ?? "-";
        var coreClock = sensorsData?.GPU.CoreClock ?? -1;
        var temperature = sensorsData?.GPU.Temperature ?? -1;
        var fanSpeed = sensorsData?.GPU.FanSpeed ?? -1;

        _gpuPowerStateValueLabel.Content = performanceState;
        UpdateFreqAndTemp(_gpuFreqAndTempLabel, coreClock, temperature);
        UpdateFanAndPower(_gpuFanAndPowerLabel, fanSpeed, gpuPower);

        _gpuActive.Visibility = state is GPUState.Active or GPUState.MonitorConnected ? Visibility.Visible : Visibility.Collapsed;
        _gpuInactive.Visibility = state == GPUState.Inactive ? Visibility.Visible : Visibility.Collapsed;
        _gpuPoweredOff.Visibility = state == GPUState.PoweredOff ? Visibility.Visible : Visibility.Collapsed;

        var detailsVisibility = state == GPUState.PoweredOff ? Visibility.Collapsed : Visibility.Visible;
        _gpuPowerStateValue.Visibility = detailsVisibility;
        _gpuPowerStateValueLabel.Visibility = detailsVisibility;
        _gpuFreqAndTempLabel.Visibility = detailsVisibility;
        _gpuFanAndPowerLabel.Visibility = detailsVisibility;

        _gpuGrid.Visibility = Visibility.Visible;
    }

    private void RefreshBattery(BatteryInformation? batteryInformation, BatteryState? batteryState)
    {
        if (!batteryInformation.HasValue || !batteryState.HasValue)
        {
            _batteryIcon.Symbol = SymbolRegular.Battery024;
            _batteryValueLabel.Content = "-";
            _batteryModeValueLabel.Content = "-";
            _batteryDischargeValueLabel.Content = "-";
            _batteryMinDischargeValueLabel.Content = "-";
            _batteryMaxDischargeValueLabel.Content = "-";
            return;
        }

        var info = batteryInformation.Value;
        var percentage = info.BatteryPercentage;

        var symbol = (int)Math.Round(percentage / 10.0) switch
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
            _ => SymbolRegular.Battery024,
        };

        if (info.IsCharging)
            symbol = batteryState == BatteryState.Conservation ? SymbolRegular.BatterySaver24 : SymbolRegular.BatteryCharge24;

        if (info.IsLowBattery)
            _batteryValueLabel.SetResourceReference(ForegroundProperty, "SystemFillColorCautionBrush");
        else
            _batteryValueLabel.ClearValue(ForegroundProperty);

        _batteryIcon.Symbol = symbol;
        _batteryValueLabel.Content = $"{percentage}%";
        _batteryModeValueLabel.Content = batteryState.GetDisplayName();
        _batteryDischargeValueLabel.Content = $"{info.DischargeRate / 1000.0:+0.00;-0.00;0.00} W";
        _batteryMinDischargeValueLabel.Content = $"{info.MinDischargeRate / 1000.0:+0.00;-0.00;0.00} W";
        _batteryMaxDischargeValueLabel.Content = $"{info.MaxDischargeRate / 1000.0:+0.00;-0.00;0.00} W";
    }

    private void RefreshUpdate(bool hasUpdate) => _updateIndicator.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;

    private static string GetTemperatureText(double temperature)
    {
        var _applicationSettings = IoCContainer.Resolve<ApplicationSettings>();
        if (temperature <= 0) return "-";
        if (_applicationSettings.Store.TemperatureUnit == TemperatureUnit.F)
        {
            temperature = temperature * 9 / 5 + 32;
            return $"{temperature:0}{Resource.Fahrenheit}";
        }
        return $"{temperature:0}{Resource.Celsius}";
    }

    private static void UpdateFreqAndTemp(System.Windows.Controls.Label label, double freq, double temp)
    {
        label.Content = temp < 0 || freq < 0
            ? "-"
            : $"{freq:0}Mhz | {GetTemperatureText(temp)}";
    }

    private static void UpdateFanAndPower(System.Windows.Controls.Label label, double fan, double power)
    {
        label.Content = fan < 0 || power < 0
            ? "-"
            : $"{fan:0}RPM | {power:0}W";
    }

    private static void UpdateSystemFan(System.Windows.Controls.Label label, double fan)
    {
        label.Content = fan < 0
            ? "-"
            : $"{fan:0}RPM";
    }
}