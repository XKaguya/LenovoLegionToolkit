using System;
using System.Linq;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Utils;
using ManagedNativeWifi;

namespace LenovoLegionToolkit.Lib.System;

public static class WiFi
{
    public static void TurnOn()
    {
        try
        {
            NativeWifi.EnumerateInterfaces()
            .ForEach(i => NativeWifi.TurnOnInterfaceRadio(i.Id));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to turn on WiFi.", ex);
        }
    }

    public static void TurnOff()
    {
        try
        {
            NativeWifi.EnumerateInterfaces()
                .ForEach(i => NativeWifi.TurnOffInterfaceRadio(i.Id));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to turn off WiFi.", ex);
        }
    }

    public static string? GetConnectedNetworkSsid()
    {
        return NativeWifi.EnumerateConnectedNetworkSsids()
            .Select(c => c.ToString())
            .FirstOrDefault();
    }

    public static void Toggle()
    {
        try
        {
            var interfaces = NativeWifi.EnumerateInterfaces().ToList();
            var anyOn = interfaces.Any(i => NativeWifi.GetInterfaceRadio(i.Id)?.RadioSets.Any(r => r.SoftwareOn == true) == true);
            if (anyOn)
                TurnOff();
            else
                TurnOn();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to toggle WiFi.", ex);
        }
    }
}
