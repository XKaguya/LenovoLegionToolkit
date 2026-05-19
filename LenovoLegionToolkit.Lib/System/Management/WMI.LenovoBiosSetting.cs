using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.System.Management;

public static partial class WMI
{
    public static class LenovoBiosSetting
    {
        public static async Task<List<string>> GetBiosSelectionsAsync(string settingName)
        {
            var result = await CallAsync(
                "root\\WMI",
                $"SELECT * FROM Lenovo_GetBiosSelections",
                "GetBiosSelections",
                new() { { "Item", settingName } },
                pdc =>
                {
                    string? raw = pdc["Selections"]?.Value?.ToString();
                    if (string.IsNullOrEmpty(raw))
                        return [];

                    return raw
                        .Split(',')
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList();
                }).ConfigureAwait(false);

            Log.Instance.Trace($"GetBiosSelectionsAsync result for [{settingName}]: {string.Join(", ", result)}");
            return result;
        }

        public static async Task<string> GetBiosSettingAsync(string settingName)
        {
            var scope = new ManagementScope("root\\WMI");
            var query = new ObjectQuery("SELECT * FROM Lenovo_BiosSetting");
            using var searcher = new ManagementObjectSearcher(scope, query);

            var managementObjects = await searcher.GetAsync().ConfigureAwait(false);

            foreach (var managementObject in managementObjects)
            {
                using var obj = (ManagementObject)managementObject;
                var current = obj["CurrentSetting"]?.ToString();
                if (current != null && current.StartsWith(settingName + ","))
                {
                    var parts = current.Split(',');
                    if (parts.Length >= 2)
                    {
                        Log.Instance.Trace($"GetBiosSettingAsync result for [{settingName}]: {parts[1]}");
                        return parts[1];
                    }
                }
            }

            Log.Instance.Trace($"GetBiosSettingAsync result for [{settingName}]: Empty/Not Found");
            return string.Empty;
        }

        public static async Task SetBiosSettingAsync(string settingName, string value)
        {
            await CallAsync(
                "root\\WMI",
                $"SELECT * FROM Lenovo_SetBiosSetting",
                "SetBiosSetting",
                new() { { "parameter", $"{settingName},{value}," } }).ConfigureAwait(false);

            Log.Instance.Trace($"SetBiosSettingAsync executed for [{settingName}] with value [{value}]");
        }

        public static async Task SaveBiosSettingAsync()
        {
            await CallAsync(
                "root\\WMI",
                $"SELECT * FROM Lenovo_SaveBiosSettings",
                "SaveBiosSettings",
                new()).ConfigureAwait(false);

            Log.Instance.Trace($"SaveBiosSettingAsync executed successfully");
        }
    }
}