using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using System.Management;

Console.WriteLine(@"Probe is a utility program used to gather all information that Lenovo Legion Toolkit requires.");
Console.WriteLine(@"Press any key to continue...");
Console.ReadKey();

Console.WriteLine(@"=========================================");
Console.WriteLine(@"Reading Fan Table...");
var fanTableData = await WMI.LenovoFanTableData.ReadAsync().ConfigureAwait(false);
Log.Instance.Trace($"Fan table data: {string.Join(", ", fanTableData)}");

Console.WriteLine(@"=========================================");
Console.WriteLine(@"Reading Keyboard Hardware Id...");

var searcher = new ManagementObjectSearcher(@"Select * From Win32_PnPEntity Where DeviceID Like 'HID%'");

foreach (var device in searcher.Get())
{
    string name = device["Name"]?.ToString() ?? "Unknown Device";
    string deviceID = device["DeviceID"]?.ToString() ?? "N/A";
    string status = device["Status"]?.ToString() ?? "N/A";

    Console.WriteLine(@$"Device: {name}");
    Console.WriteLine(@$"Hardware Id: {deviceID}");
    Console.WriteLine(@$"Status: {status}");
    Console.WriteLine(new string('-', 50));
}