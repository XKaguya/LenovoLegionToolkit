using LenovoLegionToolkit.Lib.Extensions;
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
        public static Task<List<string>> GetBiosSelections(string settingName) => CallAsync(
            "root\\WMI",
            $"SELECT * FROM Lenovo_GetBiosSelections",
            "GetBiosSelections",
            new Dictionary<string, object> { { "Item", settingName } },
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
            });

        public static async Task<string> GetBiosSetting(string settingName)
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
                        return parts[1];
                }
            }
            return string.Empty;
        }

        public static Task SetBiosSetting(string settingName, string value) => CallAsync(
            "root\\WMI",
            $"SELECT * FROM Lenovo_SetBiosSetting",
            "SetBiosSetting",
            new Dictionary<string, object> { { "parameter", $"{settingName},{value}," } });

        public static Task SaveBiosSetting() => CallAsync(
            "root\\WMI",
            $"SELECT * FROM Lenovo_SaveBiosSettings",
            "SaveBiosSettings",
            []);
    }
}