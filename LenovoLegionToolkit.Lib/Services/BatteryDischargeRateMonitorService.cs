using System;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Services;

public class BatteryDischargeRateMonitorService
{
    private CancellationTokenSource? _cts;
    private Task? _refreshTask;

    public async Task StartStopIfNeededAsync()
    {
        await StopAsync().ConfigureAwait(false);

        if (_refreshTask != null)
            return;

        if (_cts is not null)
            await _cts.CancelAsync().ConfigureAwait(false);

        _cts = new CancellationTokenSource();

        var token = _cts.Token;

        _refreshTask = Task.Run(async () =>
        {
            Log.Instance.Trace($"Battery monitoring service started...");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    Battery.SetMinMaxDischargeRate();

                    await Task.Delay(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Battery monitoring service failed.", ex);
                }
            }

            Log.Instance.Trace($"Battery monitoring service stopped.");
        }, token);
    }

    public async Task StopAsync()
    {
        Log.Instance.Trace($"Stopping...");

        if (_cts is not null)
            await _cts.CancelAsync().ConfigureAwait(false);

        _cts = null;

        if (_refreshTask is not null)
            await _refreshTask.ConfigureAwait(false);

        _refreshTask = null;

        Log.Instance.Trace($"Stopped.");
    }
}
