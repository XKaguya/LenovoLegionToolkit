using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Listeners;

public class DisplayBrightnessListener(WindowsPowerPlanController windowsPowerPlanController, ApplicationSettings settings)
    : AbstractWMIListener<DisplayBrightnessListener.ChangedEventArgs, Brightness, byte>(WMI.WmiMonitorBrightnessEvent.Listen)
{
    public class ChangedEventArgs(Brightness brightness) : EventArgs
    {
        public Brightness Brightness { get; } = brightness;
    }

    private readonly ThrottleLastDispatcher _dispatcher = new(TimeSpan.FromSeconds(2), nameof(DisplayBrightnessListener));

    protected override Brightness GetValue(byte value) => new(value);

    protected override ChangedEventArgs GetEventArgs(Brightness value) => new(value);

    protected override async Task OnChangedAsync(Brightness value) => await SynchronizeBrightnessAsync(value).ConfigureAwait(false);

    private async Task SynchronizeBrightnessAsync(Brightness value)
    {
        if (!settings.Store.SynchronizeBrightnessToAllPowerPlans)
            return;

        await _dispatcher.DispatchAsync(() =>
        {
            SetBrightnessForAllPowerPlans(value);
            return Task.CompletedTask;
        }).ConfigureAwait(false);
    }

    private void SetBrightnessForAllPowerPlans(Brightness brightness)
    {
        try
        {
            Log.Instance.Trace($"Setting brightness to {brightness.Value}...");

            var powerPlans = windowsPowerPlanController.GetPowerPlans();

            foreach (var powerPlan in powerPlans)
            {
                Log.Instance.Trace($"Modifying power plan {powerPlan.Name}... [powerPlan.Guid={powerPlan.Guid}, brightness={brightness.Value}]");

                windowsPowerPlanController.SetPowerPlanParameter(powerPlan, brightness);
            }

            Log.Instance.Trace($"Brightness set to {brightness.Value}.");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to set brightness to {brightness.Value}.", ex);
        }
    }
}
