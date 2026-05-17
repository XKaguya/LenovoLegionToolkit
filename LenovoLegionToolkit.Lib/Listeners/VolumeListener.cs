using System;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using NAudio.CoreAudioApi;

namespace LenovoLegionToolkit.Lib.Listeners;

public class VolumeListener : IListener<VolumeListener.ChangedEventArgs>, IDisposable
{
    public class ChangedEventArgs(bool speakerMute) : EventArgs
    {
        public bool SpeakerMute { get; } = speakerMute;
    }

    public event EventHandler<ChangedEventArgs>? Changed;

    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _speakerDevice;
    private bool _disposed;

    private MachineInformation? _cachedMachineInformation;
    private bool? _lastMuteState;

    public async Task StartAsync()
    {
        try
        {
            _cachedMachineInformation = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
            _enumerator = new MMDeviceEnumerator();
            _speakerDevice = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).FirstOrDefault();

            if (_speakerDevice != null)
            {
                _lastMuteState = _speakerDevice.AudioEndpointVolume.Mute;
                _speakerDevice.AudioEndpointVolume.OnVolumeNotification += OnSpeakerVolumeNotification;
            }

            Log.Instance.Trace($"VolumeListener Starting...");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"VolumeListener start failed: {ex.Message}", ex);
        }
    }

    public Task StopAsync()
    {
        if (_speakerDevice != null)
            _speakerDevice.AudioEndpointVolume.OnVolumeNotification -= OnSpeakerVolumeNotification;

        _speakerDevice?.Dispose();
        _enumerator?.Dispose();

        return Task.CompletedTask;
    }

    public async Task NotifyAsync(bool speakerMute)
    {
        await OnChangedAsync(speakerMute).ConfigureAwait(false);
    }

    protected virtual Task OnChangedAsync(bool speakerMute)
    {
        RaiseChanged(speakerMute);
        return Task.CompletedTask;
    }

    protected void RaiseChanged(bool speakerMute)
    {
        Changed?.Invoke(this, new ChangedEventArgs(speakerMute));
    }

    private async void OnSpeakerVolumeNotification(AudioVolumeNotificationData data)
    {
        try
        {
            if (_lastMuteState == data.Muted)
            {
                return;
            }

            _lastMuteState = data.Muted;

            _cachedMachineInformation ??= await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

            if (_cachedMachineInformation.Value.LegionSeries > LegionSeries.Legion_Legacy)
            {
                var ledState = data.Muted ? SpecialKeyLedState.SpeakerOff : SpecialKeyLedState.SpeakerOn;
                await WMI.LenovoUtilityData.SetFeatureAsync(ledState).ConfigureAwait(false);
            }

            await OnChangedAsync(data.Muted).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"VolumeListener error: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        StopAsync().GetAwaiter().GetResult();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}