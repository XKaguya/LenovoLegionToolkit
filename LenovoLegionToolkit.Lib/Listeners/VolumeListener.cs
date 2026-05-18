using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace LenovoLegionToolkit.Lib.Listeners;

public class VolumeListener : IListener<VolumeListener.ChangedEventArgs>, IMMNotificationClient, IDisposable
{
    public class ChangedEventArgs(bool speakerMute) : EventArgs
    {
        public bool SpeakerMute { get; } = speakerMute;
    }

    public event EventHandler<ChangedEventArgs>? Changed;

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _speakerDevice;
    private bool _disposed;

    private bool? _lastMuteState;

    public async Task StartAsync()
    {
        try
        {
            _enumerator = new MMDeviceEnumerator();
            _enumerator.RegisterEndpointNotificationCallback(this);
            _speakerDevice = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).FirstOrDefault();

            if (_speakerDevice != null)
            {
                _lastMuteState = _speakerDevice.AudioEndpointVolume.Mute;
                _speakerDevice.AudioEndpointVolume.OnVolumeNotification += OnSpeakerVolumeNotification;
                Log.Instance.Trace($"VolumeListener started. [device={_speakerDevice.FriendlyName}, mute={_lastMuteState}]");
            }
            else
            {
                Log.Instance.Trace($"VolumeListener started, no active render device found.");
            }
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

        if (_enumerator != null)
        {
            _enumerator.UnregisterEndpointNotificationCallback(this);
            _enumerator.Dispose();
        }

        try
        {
            _initLock.Dispose();
        }
        catch (ObjectDisposedException) { }

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
            Log.Instance.Trace($"VolumeListener notification: muted={data.Muted}, lastMute={_lastMuteState}");

            if (_lastMuteState == data.Muted)
            {
                return;
            }

            _lastMuteState = data.Muted;

            await SpecialKeyLedHelper.SetLedAsync(data.Muted ? SpecialKeyLedState.SpeakerOn : SpecialKeyLedState.SpeakerOff).ConfigureAwait(false);

            await OnChangedAsync(data.Muted).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"VolumeListener error: {ex.Message}", ex);
        }
    }

    public void OnDefaultDeviceChanged(DataFlow dataFlow, Role deviceRole, string defaultDeviceId)
    {
        Log.Instance.Trace($"VolumeListener OnDefaultDeviceChanged: flow={dataFlow}, role={deviceRole}, id={defaultDeviceId}");

        if (dataFlow != DataFlow.Render) return;

        Task.Run(async () =>
        {
            if (!await _initLock.WaitAsync(0))
            {
                Log.Instance.Trace($"VolumeListener re-init skipped, already in progress.");
                return;
            }

            try
            {
                Reinitialize(defaultDeviceId);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"VolumeListener re-init error: {ex.Message}", ex);
            }
            finally
            {
                try { _initLock.Release(); } catch (ObjectDisposedException) { }
            }
        });
    }

    public void OnDeviceAdded(string deviceId)
    {
        Log.Instance.Trace($"VolumeListener OnDeviceAdded: id={deviceId}");
    }

    public void OnDeviceRemoved(string deviceId)
    {
        Log.Instance.Trace($"VolumeListener OnDeviceRemoved: id={deviceId}");
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        Log.Instance.Trace($"VolumeListener OnDeviceStateChanged: id={deviceId}, state={newState}");
    }

    public void OnPropertyValueChanged(string deviceId, PropertyKey propertyKey)
    {
    }

    private void Reinitialize(string targetDeviceId)
    {
        if (_speakerDevice != null)
        {
            _speakerDevice.AudioEndpointVolume.OnVolumeNotification -= OnSpeakerVolumeNotification;
            Log.Instance.Trace($"VolumeListener unsubscribed from: {_speakerDevice.FriendlyName}");
        }
        _speakerDevice?.Dispose();

        var previousMute = _lastMuteState;

        _speakerDevice = _enumerator!.GetDevice(targetDeviceId);
        if (_speakerDevice == null)
        {
            Log.Instance.Trace($"VolumeListener: GetDevice({targetDeviceId}) returned null, falling back to enumeration.");
            _speakerDevice = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).FirstOrDefault();
        }

        if (_speakerDevice != null)
        {
            _lastMuteState = _speakerDevice.AudioEndpointVolume.Mute;
            _speakerDevice.AudioEndpointVolume.OnVolumeNotification += OnSpeakerVolumeNotification;
            Log.Instance.Trace($"VolumeListener re-subscribed. [device={_speakerDevice.FriendlyName}, mute={_lastMuteState}, previousMute={previousMute}]");

            if (previousMute != _lastMuteState && _lastMuteState.HasValue)
            {
                Log.Instance.Trace($"VolumeListener mute state changed after re-init. [old={previousMute}, new={_lastMuteState}]");
                _ = OnChangedAsync(_lastMuteState.Value);
            }
        }
        else
        {
            _lastMuteState = null;
            Log.Instance.Trace($"VolumeListener: no render device available after re-init.");
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
