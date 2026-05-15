using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Listeners;

public class ITSModeListener(ITSModeFeature itsModeFeature) : IListener<ITSModeListener.ChangedEventArgs>, IDisposable
{
    public class ChangedEventArgs(ITSMode state) : EventArgs
    {
        public ITSMode State { get; } = state;
    }

    public event EventHandler<ChangedEventArgs>? Changed;

    public Task StartAsync()
    {
        MessagingCenter.Subscribe<DriverKeyPressedMessage>(this, OnFnQKeyPressedAsync);
        Log.Instance.Trace($"ITSModeListener started, listening for driver keys.");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        MessagingCenter.Unsubscribe<DriverKeyPressedMessage>(this);
        return Task.CompletedTask;
    }

    private async void OnFnQKeyPressedAsync(DriverKeyPressedMessage message)
    {
        if (message.Key != DriverKey.FnQ)
        {
            return;
        }

        try
        {
            if (!await itsModeFeature.IsSupportedAsync().ConfigureAwait(false))
            {
                return;
            }

            var newState = await itsModeFeature.ToggleItsMode().ConfigureAwait(false);

            if (newState == ITSMode.None)
            {
                return;
            }

            Log.Instance.Trace($"ITSModeListener detected Fn+Q, preparing to broadcast {newState}");

            await OnChangedAsync(newState).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Error handling Fn+Q in ITSModeListener", ex);
        }
    }

    protected virtual Task OnChangedAsync(ITSMode value)
    {
        itsModeFeature.LastItsMode = value;
        RaiseChanged(value);

        return Task.CompletedTask;
    }

    public async Task NotifyAsync(ITSMode value)
    {
        await OnChangedAsync(value).ConfigureAwait(false);
    }

    protected void RaiseChanged(ITSMode value)
    {
        Changed?.Invoke(this, new ChangedEventArgs(value));
    }

    public void Dispose()
    {
        MessagingCenter.Unsubscribe<DriverKeyPressedMessage>(this);
        GC.SuppressFinalize(this);
    }
}