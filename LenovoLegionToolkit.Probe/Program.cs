using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.System.Management;
using Newtonsoft.Json.Linq;
using System.Management;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine(@"============================================================================");
Console.WriteLine(@"Probe - Lenovo Legion Toolkit Hardware Information Gatherer");
Console.WriteLine(@"============================================================================");
Console.WriteLine(@"Press any key to start scanning...");
Console.ReadKey();

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
    Console.WriteLine(@$"Error reading Fan Table: {ex.Message}");
}

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
    Console.WriteLine(@$"Error scanning HID devices: {ex.Message}");
}

Console.WriteLine(@">>> Section 3: Support Power Modes");
Console.WriteLine(@"----------------------------------------------------------------------------");
try
{
    var value = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.SupportedPowerModes).ConfigureAwait(false);
    Console.WriteLine(@$"Supported Power Modes: {value}");
}
catch { /* Ignore */}

try
{
    var result = await WMI.LenovoOtherMethod.GetSupportThermalModeAsync().ConfigureAwait(false);
    Console.WriteLine(@$"Supported Power Modes: {result}");
}
catch { /* Ignore */}

Console.WriteLine();
Console.WriteLine(@"============================================================================");
Console.WriteLine(@"Scan Complete. Press any key to exit...");
Console.ReadKey();