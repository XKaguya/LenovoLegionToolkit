#if !DEBUG
using LenovoLegionToolkit.Lib.System;
#endif
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Features.Hybrid;
using LenovoLegionToolkit.Lib.Features.Hybrid.Notify;
using LenovoLegionToolkit.Lib.Features.PanelLogo;
using LenovoLegionToolkit.Lib.Features.WhiteKeyboardBacklight;
using LenovoLegionToolkit.Lib.Integrations;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Macro;
using LenovoLegionToolkit.Lib.Services;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.CLI;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Pages;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows;
using LenovoLegionToolkit.WPF.Windows.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using WinFormsApp = System.Windows.Forms.Application;
using WinFormsHighDpiMode = System.Windows.Forms.HighDpiMode;

namespace LenovoLegionToolkit.WPF;

public partial class App
{
    private const string MUTEX_NAME = "LenovoLegionToolkit_Mutex_6efcc882-924c-4cbc-8fec-f45c25696f98";
    private const string EVENT_NAME = "LenovoLegionToolkit_Event_6efcc882-924c-4cbc-8fec-f45c25696f98";

    public Window? FloatingGadget = null;

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _singleInstanceWaitHandle;

    private bool _showPawnIONotify;

    public new static App Current => (App)Application.Current;
    public static MainWindow? MainWindowInstance = null;

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
#if DEBUG
        if (Debugger.IsAttached)
        {
            Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName)
                .Where(p => p.Id != Environment.ProcessId)
                .ForEach(p =>
                {
                    p.Kill();
                    p.WaitForExit();
                });
        }
#endif

        var flags = new Flags(e.Args);

        SetupExceptionHandling();

        Log.Instance.IsTraceEnabled = flags.IsTraceEnabled;

        EnsureSingleInstance();

        IoCContainer.Initialize(
            new Lib.IoCModule(),
            new Lib.Automation.IoCModule(),
            new Lib.Macro.IoCModule(),
            new IoCModule()
        );

        var localizationTask = LocalizationHelper.SetLanguageAsync(true);
        var compatibilityTask = CheckCompatibilityAsyncWrapper(flags);

        await Task.WhenAll(localizationTask, compatibilityTask);

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Starting... [version={Assembly.GetEntryAssembly()?.GetName().Version}, build={Assembly.GetEntryAssembly()?.GetBuildDateTimeString()}, os={Environment.OSVersion}, dotnet={Environment.Version}]");

        WinFormsApp.SetHighDpiMode(WinFormsHighDpiMode.PerMonitorV2);
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        IoCContainer.Resolve<HttpClientFactory>().SetProxy(flags.ProxyUrl, flags.ProxyUsername, flags.ProxyPassword, flags.ProxyAllowAllCerts);
        IoCContainer.Resolve<PowerModeFeature>().AllowAllPowerModesOnBattery = flags.AllowAllPowerModesOnBattery;
        IoCContainer.Resolve<RGBKeyboardBacklightController>().ForceDisable = flags.ForceDisableRgbKeyboardSupport;
        IoCContainer.Resolve<SpectrumKeyboardBacklightController>().ForceDisable = flags.ForceDisableSpectrumKeyboardSupport;
        IoCContainer.Resolve<WhiteKeyboardLenovoLightingBacklightFeature>().ForceDisable = flags.ForceDisableLenovoLighting;
        IoCContainer.Resolve<PanelLogoLenovoLightingBacklightFeature>().ForceDisable = flags.ForceDisableLenovoLighting;
        IoCContainer.Resolve<PortsBacklightFeature>().ForceDisable = flags.ForceDisableLenovoLighting;
        IoCContainer.Resolve<IGPUModeFeature>().ExperimentalGPUWorkingMode = flags.ExperimentalGPUWorkingMode;
        IoCContainer.Resolve<DGPUNotify>().ExperimentalGPUWorkingMode = flags.ExperimentalGPUWorkingMode;
        IoCContainer.Resolve<UpdateChecker>().Disable = flags.DisableUpdateChecker;

        AutomationPage.EnableHybridModeAutomation = flags.EnableHybridModeAutomation;

        var initTasks = new List<Task>
        {
            InitSensorsGroupControllerFeatureAsync(),
            LogSoftwareStatusAsync(),
            InitPowerModeFeatureAsync(),
            InitITSModeFeatureAsync(),
            InitBatteryFeatureAsync(),
            InitRgbKeyboardControllerAsync(),
            InitSpectrumKeyboardControllerAsync(),
            InitGpuOverclockControllerAsync(),
            InitHybridModeAsync(),
            InitAutomationProcessorAsync()
        };

        await Task.WhenAll(initTasks);

        InitMacroController();

        var deferredInitTask = Task.Run(async () =>
        {
            await IoCContainer.Resolve<AIController>().StartIfNeededAsync();
            await IoCContainer.Resolve<HWiNFOIntegration>().StartStopIfNeededAsync();
            await IoCContainer.Resolve<IpcServer>().StartStopIfNeededAsync();
        });

        await InitSetPowerMode();

#if !DEBUG
        Autorun.Validate();
#endif

        var mainWindow = new MainWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            TrayTooltipEnabled = !flags.DisableTrayTooltip,
            DisableConflictingSoftwareWarning = flags.DisableConflictingSoftwareWarning
        };
        MainWindow = mainWindow;
        MainWindowInstance = mainWindow;

        IoCContainer.Resolve<ThemeManager>().Apply();

        InitSetLogIndicator();

        if (flags.Minimized)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Sending MainWindow to tray...");

            mainWindow.WindowState = WindowState.Minimized;
            mainWindow.Show();
            mainWindow.SendToTray();
        }
        else
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Showing MainWindow...");

            mainWindow.Show();
            if (_showPawnIONotify)
            {
                ShowPawnIONotify();
            }
        }

        await deferredInitTask;

        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Lenovo Legion Toolit Version {Assembly.GetEntryAssembly()?.GetName().Version}");
        }

        Compatibility.PrintControllerVersion();
        CheckFloatingGadget();

        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Start up complete");
        }
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        _singleInstanceMutex?.Close();
    }

    private async Task CheckCompatibilityAsyncWrapper(Flags flags)
    {
        if (flags.SkipCompatibilityCheck)
            return;

        try
        {
            if (!await CheckBasicCompatibilityAsync())
                return;
            if (!await CheckCompatibilityAsync())
                return;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to check device compatibility", ex);

            MessageBox.Show(Resource.CompatibilityCheckError_Message, Resource.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(200);
        }
    }

    public void RestartMainWindow()
    {
        if (MainWindow is MainWindow mw)
        {
            mw.SuppressClosingEventHandler = true;
            mw.Close();
        }

        var mainWindow = new MainWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        MainWindow = mainWindow;
        MainWindowInstance = mainWindow;
        mainWindow.Show();

        if (FloatingGadget != null)
        {
            FloatingGadget.Hide();

            var type = FloatingGadget.GetType();
            var windowConstructors = new Dictionary<Type, Func<Window>>
            {
                { typeof(FloatingGadget), () => new FloatingGadget() },
                { typeof(FloatingGadgetUpper), () => new FloatingGadgetUpper() }
            };

            if (windowConstructors.TryGetValue(type, out var constructor))
            {
                FloatingGadget.Close();
                FloatingGadget = constructor();
                FloatingGadget.Show();
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
    }

    public async Task ShutdownAsync()
    {
        try
        {
            if (IoCContainer.TryResolve<AIController>() is { } aiController)
                await aiController.StopAsync();
        }
        catch {  /* Ignored. */ }

        try
        {
            if (IoCContainer.TryResolve<RGBKeyboardBacklightController>() is { } rgbKeyboardBacklightController)
            {
                if (await rgbKeyboardBacklightController.IsSupportedAsync())
                    await rgbKeyboardBacklightController.SetLightControlOwnerAsync(false);
            }
        }
        catch {  /* Ignored. */ }

        try
        {
            if (IoCContainer.TryResolve<SpectrumKeyboardBacklightController>() is { } spectrumKeyboardBacklightController)
            {
                if (await spectrumKeyboardBacklightController.IsSupportedAsync())
                    await spectrumKeyboardBacklightController.StopAuroraIfNeededAsync();
            }
        }
        catch {  /* Ignored. */ }

        try
        {
            if (IoCContainer.TryResolve<NativeWindowsMessageListener>() is { } nativeMessageWindowListener)
            {
                await nativeMessageWindowListener.StopAsync();
            }
        }
        catch {  /* Ignored. */ }

        try
        {
            if (IoCContainer.TryResolve<SessionLockUnlockListener>() is { } sessionLockUnlockListener)
            {
                await sessionLockUnlockListener.StopAsync();
            }
        }
        catch { /* Ignored. */ }

        try
        {
            if (IoCContainer.TryResolve<HWiNFOIntegration>() is { } hwinfoIntegration)
            {
                await hwinfoIntegration.StopAsync();
            }
        }
        catch { /* Ignored. */ }

        try
        {
            if (IoCContainer.TryResolve<IpcServer>() is { } ipcServer)
            {
                await ipcServer.StopAsync();
            }
        }
        catch { /* Ignored. */ }

        try
        {
            if (IoCContainer.TryResolve<BatteryDischargeRateMonitorService>() is { } batteryDischargeMon)
            {
                await batteryDischargeMon.StopAsync();
            }
        }
        catch { /* Ignored. */ }

        Shutdown();
    }

    private void LogUnhandledException(Exception exception)
    {
        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Exception in LogUnhandledException {exception.Message}", exception);
        }

        if (Application.Current != null)
        {
            if (Application.Current.Dispatcher.CheckAccess())
            {
                SnackbarHelper.Show(Resource.UnexpectedException, exception?.Message + exception?.StackTrace ?? "Unknown exception.", SnackbarType.Error);
            }
            else
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    SnackbarHelper.Show(Resource.UnexpectedException, exception?.Message + exception?.StackTrace ?? "Unknown exception.", SnackbarType.Error);
                }));
            }
        }
    }

    private void SetupExceptionHandling()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) => LogUnhandledException((Exception)e.ExceptionObject);

        DispatcherUnhandledException += (s, e) =>
        {
            LogUnhandledException(e.Exception);
            e.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogUnhandledException(e.Exception);
            e.SetObserved();
        };
    }

    private async Task<bool> CheckBasicCompatibilityAsync()
    {
        var isCompatible = await Compatibility.CheckBasicCompatibilityAsync();
        if (isCompatible)
            return true;

        MessageBox.Show(Resource.IncompatibleDevice_Message, Resource.AppName, MessageBoxButton.OK, MessageBoxImage.Error);

        Shutdown(201);
        return false;
    }

    private void CheckFloatingGadget()
    {
        ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
        if (_settings.Store.ShowFloatingGadgets)
        {
            if (FloatingGadget != null)
            {
                FloatingGadget.Show();
            }
            else
            {
                if (_settings.Store.SelectedStyleIndex == 0)
                {
                    FloatingGadget = new FloatingGadget();
                }
                else if (_settings.Store.SelectedStyleIndex == 1)
                {
                    FloatingGadget = new FloatingGadgetUpper();
                }
                else
                {
                    FloatingGadget = new FloatingGadget();
                }

                FloatingGadget!.Show();
            }
        }
    }

    private async Task<bool> CheckCompatibilityAsync()
    {
        var (isCompatible, mi) = await Compatibility.IsCompatibleAsync();
        if (isCompatible)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Compatibility check passed. [Vendor={mi.Vendor}, Model={mi.Model}, MachineType={mi.MachineType}, BIOS={mi.BiosVersion}]");
            return true;
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Incompatible system detected. [Vendor={mi.Vendor}, Model={mi.Model}, MachineType={mi.MachineType}, BIOS={mi.BiosVersion}]");

        var unsupportedWindow = new UnsupportedWindow(mi);
        unsupportedWindow.Show();

        var result = await unsupportedWindow.ShouldContinue;
        if (result)
        {
            Log.Instance.IsTraceEnabled = true;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Compatibility check OVERRIDE. [Vendor={mi.Vendor}, Model={mi.Model}, MachineType={mi.MachineType}, version={Assembly.GetEntryAssembly()?.GetName().Version}, build={Assembly.GetEntryAssembly()?.GetBuildDateTimeString() ?? string.Empty}]");
            return true;
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Shutting down... [Vendor={mi.Vendor}, Model={mi.Model}, MachineType={mi.MachineType}]");

        Shutdown(202);
        return false;
    }

    private void EnsureSingleInstance()
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Checking for other instances...");

        _singleInstanceMutex = new Mutex(true, MUTEX_NAME, out var isOwned);
        _singleInstanceWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, EVENT_NAME);

        if (!isOwned)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Another instance running, closing...");

            _singleInstanceWaitHandle.Set();
            Shutdown();
            return;
        }

        new Thread(() =>
        {
            while (_singleInstanceWaitHandle.WaitOne())
            {
                Current.Dispatcher.BeginInvoke(async () =>
                {
                    if (Current.MainWindow is { } window)
                    {
                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"Another instance started, bringing this one to front instead...");

                        window.BringToForeground();
                    }
                    else
                    {
                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"!!! PANIC !!! This instance is missing main window. Shutting down.");

                        await ShutdownAsync();
                    }
                });
            }
        })
        {
            IsBackground = true
        }.Start();
    }

    private static async Task LogSoftwareStatusAsync()
    {
        if (!Log.Instance.IsTraceEnabled)
            return;

        var vantageStatus = await IoCContainer.Resolve<VantageDisabler>().GetStatusAsync();
        Log.Instance.Trace($"Vantage status: {vantageStatus}");

        var legionSpaceStatus = await IoCContainer.Resolve<LegionSpaceDisabler>().GetStatusAsync();
        Log.Instance.Trace($"LegionSpace status: {legionSpaceStatus}");

        var legionZoneStatus = await IoCContainer.Resolve<LegionZoneDisabler>().GetStatusAsync();
        Log.Instance.Trace($"LegionZone status: {legionZoneStatus}");

        var fnKeysStatus = await IoCContainer.Resolve<FnKeysDisabler>().GetStatusAsync();
        Log.Instance.Trace($"FnKeys status: {fnKeysStatus}");
    }

    private static async Task InitHybridModeAsync()
    {
        try
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Initializing hybrid mode...");

            var feature = IoCContainer.Resolve<HybridModeFeature>();
            await feature.EnsureDGPUEjectedIfNeededAsync(); 
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Couldn't initialize hybrid mode.", ex);
        }
    }

    private static async Task InitAutomationProcessorAsync()
    {
        try
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Initializing automation processor...");

            var automationProcessor = IoCContainer.Resolve<AutomationProcessor>();
            await automationProcessor.InitializeAsync();
            automationProcessor.RunOnStartup();
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Couldn't initialize automation processor.", ex);
        }
    }

    private static async Task InitSetPowerMode()
    {
        try
        {
            PowerModeFeature feature = IoCContainer.Resolve<PowerModeFeature>();
            var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
            var state = await feature.GetStateAsync().ConfigureAwait(false);

            if (await Power.IsPowerAdapterConnectedAsync() == PowerAdapterStatus.Connected 
                && state == PowerModeState.GodMode 
                && mi.Properties.HasReapplyParameterIssue)
            {
                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"Reapplying GodMode...");
                }

                await feature.SetStateAsync(PowerModeState.Balance).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                await feature.SetStateAsync(PowerModeState.GodMode).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
            {
                Log.Instance.Trace($"Couldn't reapply parameters.", ex);
            }
        }
    }

    private static void InitSetLogIndicator()
    {
        try
        {
            ApplicationSettings settings = IoCContainer.Resolve<ApplicationSettings>();
            if (settings.Store.EnableLogging)
            {
                if (App.Current.MainWindow is not MainWindow mainWindow)
                    return;

                Log.Instance.IsTraceEnabled = settings.Store.EnableLogging;
                mainWindow._openLogIndicator.Visibility = BooleanToVisibilityConverter.Convert(settings.Store.EnableLogging);

                Compatibility.PrintMachineInfo();
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
            {
                Log.Instance.Trace($"Couldn't reapply parameters.", ex);
            }
        }
    }

    private static async Task InitITSModeFeatureAsync()
    {
        try
        {
            ITSModeFeature feature = IoCContainer.Resolve<ITSModeFeature>();
            if (await feature.IsSupportedAsync())
            {
                ITSMode state = await feature.GetStateAsync();
                await feature.SetStateAsync(state);
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Ensure ITS Mode is set.");
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Couldn't ensure its mode state.", ex);
        }
    }

    private static async Task InitPowerModeFeatureAsync()
    {
        try
        {
            var feature = IoCContainer.Resolve<PowerModeFeature>();
            if (await feature.IsSupportedAsync())
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Ensuring god mode state is applied...");

                await feature.EnsureGodModeStateIsAppliedAsync();
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Couldn't ensure god mode state.", ex);
        }

        try
        {
            var feature = IoCContainer.Resolve<PowerModeFeature>();
            if (await feature.IsSupportedAsync())
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Ensuring correct power plan is set...");

                await feature.EnsureCorrectWindowsPowerSettingsAreSetAsync();
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Couldn't ensure correct power plan.", ex);
        }
    }

    private static async Task InitBatteryFeatureAsync()
    {
        try
        {
            var feature = IoCContainer.Resolve<BatteryFeature>();
            if (await feature.IsSupportedAsync())
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Ensuring correct battery mode is set...");

                await feature.EnsureCorrectBatteryModeIsSetAsync();
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Couldn't ensure correct battery mode.", ex);
        }
    }

    private static async Task InitSensorsGroupControllerFeatureAsync()
    {
        var settings = IoCContainer.Resolve<ApplicationSettings>();

        try
        {
            if (settings.Store.UseNewSensorDashboard || settings.Store.ShowFloatingGadgets)
            {
                var feature = IoCContainer.Resolve<SensorsGroupController>();
                try
                {
                    LibreHardwareMonitorInitialState state = await feature.IsSupportedAsync();
                    if (state == LibreHardwareMonitorInitialState.Initialized || state == LibreHardwareMonitorInitialState.Success)
                    {
                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"Init sensor group controller feature.");
                    }
                    else
                    {
                        Current._showPawnIONotify = true;
                    }
                }
                // Why this branch can execute ?
                // Now i see.
                catch (Exception ex)
                {
                    if (Log.Instance.IsTraceEnabled)
                    {
                        Log.Instance.Trace($"InitSensorsGroupControllerFeatureAsync() raised exception:", ex);
                    }

                    if (!ex.Message.Contains("LibreHardwareMonitor initialization failed. Disabling new sensor dashboard."))
                    {
                        Current._showPawnIONotify = true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Init sensor group controller failed.", ex);
        }
    }

    private static async Task InitRgbKeyboardControllerAsync()
    {
        try
        {
            var controller = IoCContainer.Resolve<RGBKeyboardBacklightController>();
            if (await controller.IsSupportedAsync())
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Setting light control owner and restoring preset...");

                await controller.SetLightControlOwnerAsync(true, true);
            }
            else
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"RGB keyboard is not supported.");
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Couldn't set light control owner or current preset.", ex);
        }
    }

    private static async Task InitSpectrumKeyboardControllerAsync()
    {
        try
        {
            var controller = IoCContainer.Resolve<SpectrumKeyboardBacklightController>();
            if (await controller.IsSupportedAsync())
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Starting Aurora if needed...");

                var result = await controller.StartAuroraIfNeededAsync();
                if (result)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Aurora started.");
                }
                else
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Aurora not needed.");
                }
            }
            else
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Spectrum keyboard is not supported.");
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Couldn't start Aurora if needed.", ex);
        }
    }

    private static async Task InitGpuOverclockControllerAsync()
    {
        try
        {
            var controller = IoCContainer.Resolve<GPUOverclockController>();
            if (await controller.IsSupportedAsync())
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Ensuring GPU overclock is applied...");

                var result = await controller.EnsureOverclockIsAppliedAsync();
                if (result)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"GPU overclock applied.");
                }
                else
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"GPU overclock not needed.");
                }
            }
            else
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"GPU overclock is not supported.");
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Couldn't overclock GPU.", ex);
        }
    }

    private static void InitMacroController()
    {
        var controller = IoCContainer.Resolve<MacroController>();
        controller.Start();
    }

    private static void ShowPawnIONotify()
    {
        var dialog = new DialogWindow
        {
            Title = Resource.MainWindow_PawnIO_Warning_Title,
            Content = Resource.MainWindow_PawnIO_Warning_Message,
            Owner = Application.Current.MainWindow
        };

        dialog.ShowDialog();

        if (dialog.Result.Item1)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://pawnio.eu/",
                UseShellExecute = true
            });
        }
    }
}
