using System.Management;
using System.Text;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.System.Management;

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine(@"============================================================================");
Console.WriteLine(@"Probe - Lenovo Legion Toolkit Hardware Information Gatherer");
Console.WriteLine(@"============================================================================");
Console.WriteLine(@"Press any key to start scanning...");
Console.ReadKey();
string GetFullException(Exception ex)
{
    var sb = new StringBuilder();
    sb.AppendLine($"[Exception]: {ex.GetType().Name}");
    sb.AppendLine($"[Message]: {ex.Message}");
    sb.AppendLine($"[StackTrace]: {ex.StackTrace}");

    if (ex.InnerException != null)
    {
        sb.AppendLine("\n--- Inner Exception ---");
        sb.Append(GetFullException(ex.InnerException));
    }
    return sb.ToString();
}

void LogError(Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine(GetFullException(ex));
    Console.ResetColor();
}

// --- Section 1 ---
Console.WriteLine();
Console.WriteLine(@">>> Section 1: Fan Table Data");
Console.WriteLine(@"----------------------------------------------------------------------------");

try
{
    var data = await WMI.LenovoFanTableData.ReadAsync().ConfigureAwait(false);
    var fanTableData = data
        .Where(d => d.mode == 255)
        .Select(d =>
        {
            var type = (d.fanId, d.sensorId) switch
            {
                (1, 1) or (1, 4) => FanTableType.CPU,
                (2, 5) => FanTableType.GPU,
                (4, 4) or (5, 5) or (4, 1) => FanTableType.PCH,
                _ => FanTableType.Unknown,
            };
            return new FanTableData(type, d.fanId, d.sensorId, d.fanTableData, d.sensorTableData);
        })
        .ToArray();

    if (fanTableData.Length == 0)
    {
        Console.WriteLine(@"No Fan Table data found for the custom mode.");
    }
    else
    {
        foreach (var item in fanTableData)
        {
            Console.WriteLine(@$"Type: {item.Type,-8} | FanId: {item.FanId} | SensorId: {item.SensorId}");
            Console.WriteLine(@$"FanSpeeds: [{string.Join(", ", item.FanSpeeds)}]");
            Console.WriteLine(@$"Temps:     [{string.Join(", ", item.Temps)}]");
            Console.WriteLine();
        }
    }
}
catch (Exception ex)
{
    LogError(ex);
}

// --- Section 2 ---
Console.WriteLine(@">>> Section 2: HID Devices");
Console.WriteLine(@"----------------------------------------------------------------------------");

try
{
    using var searcher = new ManagementObjectSearcher(@"Select * From Win32_PnPEntity Where DeviceID Like 'HID%'");
    var found = false;

    foreach (var device in searcher.Get())
    {
        var deviceID = device["DeviceID"]?.ToString() ?? "";

        if (deviceID.Contains("48D", StringComparison.OrdinalIgnoreCase))
        {
            found = true;
            var name = device["Name"]?.ToString() ?? "Unknown HID Device";
            var status = device["Status"]?.ToString() ?? "Unknown";

            Console.WriteLine(@$"[Device]: {name}");
            Console.WriteLine(@$" [ID]:     {deviceID}");
            Console.WriteLine(@$" [Status]: {status}");
            Console.WriteLine(new string('-', 60));
        }
    }

    if (!found)
    {
        Console.WriteLine(@"No HID devices found.");
    }
}
catch (Exception ex)
{
    LogError(ex);
}

// --- Section 3 ---
Console.WriteLine(@">>> Section 3: Support Power Modes");
Console.WriteLine(@"----------------------------------------------------------------------------");
try
{
    var value = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.SupportedPowerModes).ConfigureAwait(false);
    Console.WriteLine(@$"Supported Power Modes (FeatureValue): {value}");
}
catch (Exception ex) { LogError(ex); }

try
{
    var result = await WMI.LenovoOtherMethod.GetSupportThermalModeAsync().ConfigureAwait(false);
    Console.WriteLine(@$"Supported Thermal Modes: {result}");
}
catch (Exception ex) { LogError(ex); }

// --- Section 4 ---
Console.WriteLine();
Console.WriteLine(@">>> Section 4: Support Sensors");
Console.WriteLine(@"----------------------------------------------------------------------------");
try
{
    var value = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.CpuCurrentTemperature).ConfigureAwait(false);
    Console.WriteLine(@$"CPU Current Temperature: {value}");
}
catch (Exception ex) { LogError(ex); }

try
{
    var value = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.GpuCurrentTemperature).ConfigureAwait(false);
    Console.WriteLine(@$"GPU Current Temperature: {value}");
}
catch (Exception ex) { LogError(ex); }

try
{
    var value = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.PchCurrentTemperature).ConfigureAwait(false);
    Console.WriteLine(@$"PCH Current Temperature: {value}");
}
catch (Exception ex) { LogError(ex); }

Console.WriteLine();
Console.WriteLine(@"============================================================================");
Console.WriteLine(@"Scan Complete. Press any key to exit...");
Console.ReadKey();