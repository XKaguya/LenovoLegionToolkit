using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Listeners;

public class RGBKeyboardBacklightListener(RGBKeyboardBacklightController controller)
    : AbstractWMIListener<EventArgs, RGBKeyboardBacklightChanged, int>(WMI.LenovoGameZoneLightProfileChangeEvent.Listen)
{
    protected override RGBKeyboardBacklightChanged GetValue(int value) => default;

    protected override EventArgs GetEventArgs(RGBKeyboardBacklightChanged value) => EventArgs.Empty;

    protected override async Task OnChangedAsync(RGBKeyboardBacklightChanged value)
    {
        try
        {
            if (!await controller.IsSupportedAsync().ConfigureAwait(false))
            {
                Log.Instance.Trace($"Not supported.");

                return;
            }

            Log.Instance.Trace($"Taking ownership...");

            await controller.SetLightControlOwnerAsync(true).ConfigureAwait(false);

            Log.Instance.Trace($"Setting next preset set...");

            var preset = await controller.SetNextPresetAsync().ConfigureAwait(false);

            MessagingCenter.Publish(preset == RGBKeyboardBacklightPreset.Off
                ? new NotificationMessage(NotificationType.RGBKeyboardBacklightOff, preset.GetDisplayName())
                : new NotificationMessage(NotificationType.RGBKeyboardBacklightChanged, preset.GetDisplayName()));

            Log.Instance.Trace($"Next preset set");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to set next keyboard backlight preset.", ex);
        }
    }
}
