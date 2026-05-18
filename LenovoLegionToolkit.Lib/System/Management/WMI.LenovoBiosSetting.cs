using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;

// ReSharper disable InconsistentNaming
// ReSharper disable StringLiteralTypo

namespace LenovoLegionToolkit.Lib.System.Management;

public static partial class WMI
{
    public static class LenovoBiosSetting
    {
        public static async Task<string> GetBiosSelections(string settingName)
        {
            var scope = new ManagementScope("root\\WMI");
            var query = new ObjectQuery("SELECT * FROM Lenovo_GetBiosSelections");
            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
            {
                var inParams = obj.GetMethodParameters("GetBiosSelections");
                inParams["Name"] = settingName;
                var outParams = obj.InvokeMethod("GetBiosSelections", inParams, null);
                return outParams?["Selections"]?.ToString() ?? string.Empty;
            }
            return string.Empty;
        }

        public static async Task<string> GetBiosSetting(string settingName)
        {
            var scope = new ManagementScope("root\\WMI");
            var query = new ObjectQuery("SELECT * FROM Lenovo_BiosSetting");
            using var searcher = new ManagementObjectSearcher(scope, query);

            foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
            {
                var current = obj["CurrentSetting"]?.ToString();
                if (current != null && current.StartsWith(settingName + ","))
                {
                    var parts = current.Split(',');
                    if (parts.Length >= 2)
                        return parts[1];
                }
            }
            return string.Empty;
        }

        public static async Task SetBiosSetting(string settingName, string value)
        {
            var parameter = $"{settingName},{value},";
            await WMI.CallAsync(
                "root\\WMI",
                $"SELECT * FROM Lenovo_SetBiosSetting",
                "SetBiosSetting",
                new Dictionary<string, object> { { "Parameter", parameter } });
        }

        public static async Task SaveBiosSetting()
        {
            await WMI.CallAsync(
                "root\\WMI",
                $"SELECT * FROM Lenovo_SaveBiosSettings",
                "SaveBiosSettings",
                new Dictionary<string, object>());
        }
    }
}