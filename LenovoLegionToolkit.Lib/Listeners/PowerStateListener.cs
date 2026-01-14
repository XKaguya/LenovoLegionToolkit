using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.UI.WindowsAndMessaging;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Features.Hybrid.Notify;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Overclocking.Amd;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using Microsoft.Win32;

namespace LenovoLegionToolkit.Lib.Listeners;

public class PowerStateListener : IListener<PowerStateListener.ChangedEventArgs>
{
    public class ChangedEventArgs(PowerStateEvent powerStateEvent, bool powerAdapterStateChanged) : EventArgs
    {
        public PowerStateEvent PowerStateEvent { get; } = powerStateEvent;
        public bool PowerAdapterStateChanged { get; } = powerAdapterStateChanged;
    }

    private readonly SafeHandle _recipientHandle;
    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private readonly PDEVICE_NOTIFY_CALLBACK_ROUTINE _callback;

    private readonly PowerModeFeature _powerModeFeature;
    private readonly BatteryFeature _batteryFeature;
    private readonly DGPUNotify _dgpuNotify;
    private readonly RGBKeyboardBacklightController _rgbController;

    private bool _started;
    private HPOWERNOTIFY _handle;
    private PowerAdapterStatus? _lastPowerAdapterState;

    public event EventHandler<ChangedEventArgs>? Changed;

    public unsafe PowerStateListener(PowerModeFeature powerModeFeature, BatteryFeature batteryFeature, DGPUNotify dgpuNotify, RGBKeyboardBacklightController rgbController)
    {
        _powerModeFeature = powerModeFeature;
        _batteryFeature = batteryFeature;
        _dgpuNotify = dgpuNotify;
        _rgbController = rgbController;

        _callback = Callback;
        _recipientHandle = new StructSafeHandle<DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS>(new DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS
        {
            Callback = _callback,
            Context = null,
        });
    }

    public async Task StartAsync()
    {
        if (_started)
            return;

        _lastPowerAdapterState = await Power.IsPowerAdapterConnectedAsync().ConfigureAwait(false);

        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        RegisterSuspendResumeNotification();

        _started = true;
    }

    public Task StopAsync()
    {
        SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        UnRegisterSuspendResumeNotification();

        _started = false;

        return Task.CompletedTask;
    }

    private async void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        Log.Instance.Trace($"Event received: {e.Mode}");

        var powerMode = e.Mode switch
        {
            PowerModes.StatusChange => PowerStateEvent.StatusChange,
            PowerModes.Resume => PowerStateEvent.Resume,
            PowerModes.Suspend => PowerStateEvent.Suspend,
            _ => PowerStateEvent.Unknown
        };

        if (powerMode is PowerStateEvent.Unknown)
            return;

        await HandleAsync(powerMode).ConfigureAwait(false);
    }

    private unsafe uint Callback(void* context, uint type, void* setting)
    {
        _ = Task.Run(() => CallbackAsync(type));
        return (uint)WIN32_ERROR.ERROR_SUCCESS;
    }

    private async Task CallbackAsync(uint type)
    {
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        var powerMode = type switch
        {
            PInvoke.PBT_APMSUSPEND => PowerStateEvent.Suspend,
            PInvoke.PBT_APMRESUMEAUTOMATIC => PowerStateEvent.Resume,
            PInvoke.PBT_APMPOWERSTATUSCHANGE => PowerStateEvent.StatusChange,
            _ => PowerStateEvent.Unknown
        };

        if (!mi.Properties.SupportsAlwaysOnAc.status)
        {
            Log.Instance.Trace($"Ignoring, AO AC not enabled...");

            return;
        }

        Log.Instance.Trace($"Event value: {type}");

        if (powerMode is not PowerStateEvent.Resume)
            return;

        await HandleAsync(powerMode).ConfigureAwait(false);
    }

    private async Task HandleAsync(PowerStateEvent powerStateEvent)
    {
        var powerAdapterState = await Power.IsPowerAdapterConnectedAsync().ConfigureAwait(false);

        Log.Instance.Trace($"Handle {powerStateEvent}. [newState={powerAdapterState}]");

        switch (powerStateEvent)
        {
            case PowerStateEvent.Suspend:
                await HandleSuspendAsync().ConfigureAwait(false);
                break;

            case PowerStateEvent.Resume:
                _ = Task.Run(() => SafeExecuteAsync(() => HandleResumeInternalAsync(powerAdapterState)));
                break;

            case PowerStateEvent.StatusChange when powerAdapterState is PowerAdapterStatus.Connected:
                _ = Task.Run(() => SafeExecuteAsync(HandleConnectedStatusChangeAsync));
                break;

        }

        HandlePowerStateChangeNotification(powerStateEvent, powerAdapterState);
    }

    private async Task HandleSuspendAsync()
    {
        if (await _powerModeFeature.IsSupportedAsync().ConfigureAwait(false) &&
            await _powerModeFeature.GetStateAsync().ConfigureAwait(false) == PowerModeState.GodMode)
        {
            Log.Instance.Trace($"Going to dark.");
            await _powerModeFeature.SuspendMode(PowerModeState.Balance).ConfigureAwait(false);
        }
    }

    private async Task HandleResumeInternalAsync(PowerAdapterStatus currentAdapterStatus)
    {
        if (await _batteryFeature.IsSupportedAsync().ConfigureAwait(false))
        {
            await _batteryFeature.EnsureCorrectBatteryModeIsSetAsync().ConfigureAwait(false);
        }

        if (await _rgbController.IsSupportedAsync().ConfigureAwait(false))
        {
            await _rgbController.SetLightControlOwnerAsync(true, true).ConfigureAwait(false);
        }

        if (currentAdapterStatus == PowerAdapterStatus.Connected)
        {
            var overclockingController = IoCContainer.Resolve<AmdOverclockingController>();
            if (overclockingController.IsActive())
            {
                Log.Instance.Trace($"Applying overclocking profile...");
                await overclockingController.ApplyInternalProfileAsync().ConfigureAwait(false);
            }
        }

        if (await _powerModeFeature.IsSupportedAsync().ConfigureAwait(false))
        {
            if (_powerModeFeature.LastPowerModeState == PowerModeState.GodMode)
            {
                Log.Instance.Trace($"Restore to {_powerModeFeature.LastPowerModeState}");
                await _powerModeFeature.SetStateAsync(_powerModeFeature.LastPowerModeState).ConfigureAwait(false);
            }

            await _powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync().ConfigureAwait(false);
            await _powerModeFeature.EnsureGodModeStateIsAppliedAsync().ConfigureAwait(false);
        }

        await NotifyDgpuAsync().ConfigureAwait(false);
    }

    private async Task HandleConnectedStatusChangeAsync()
    {
        if (await _powerModeFeature.IsSupportedAsync().ConfigureAwait(false))
        {
            await _powerModeFeature.EnsureGodModeStateIsAppliedAsync().ConfigureAwait(false);
        }

        await NotifyDgpuAsync().ConfigureAwait(false);
    }

    private async Task NotifyDgpuAsync()
    {
        if (await _dgpuNotify.IsSupportedAsync().ConfigureAwait(false))
        {
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await _dgpuNotify.NotifyAsync().ConfigureAwait(false);
        }
    }

    private void HandlePowerStateChangeNotification(PowerStateEvent powerStateEvent, PowerAdapterStatus newAdapterState)
    {
        var powerAdapterStateChanged = newAdapterState != _lastPowerAdapterState;
        _lastPowerAdapterState = newAdapterState;

        if (powerAdapterStateChanged)
        {
            Notify(newAdapterState);
        }

        Changed?.Invoke(this, new(powerStateEvent, powerAdapterStateChanged));
    }

    private async Task SafeExecuteAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Error executing background power task: {ex}");
        }
    }

    private unsafe void RegisterSuspendResumeNotification()
    {
        _handle = PInvoke.PowerRegisterSuspendResumeNotification(REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_CALLBACK, _recipientHandle, out var handle) == WIN32_ERROR.ERROR_SUCCESS
            ? new HPOWERNOTIFY(new IntPtr(handle))
            : HPOWERNOTIFY.Null;
    }

    private void UnRegisterSuspendResumeNotification()
    {
        PInvoke.PowerUnregisterSuspendResumeNotification(_handle);
        _handle = HPOWERNOTIFY.Null;
    }

    private static void Notify(PowerAdapterStatus newState)
    {
        switch (newState)
        {
            case PowerAdapterStatus.Connected:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.ACAdapterConnected));
                break;
            case PowerAdapterStatus.ConnectedLowWattage:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.ACAdapterConnectedLowWattage));
                break;
            case PowerAdapterStatus.Disconnected:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.ACAdapterDisconnected));
                break;
        }
    }
}
