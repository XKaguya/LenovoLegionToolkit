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
            try
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
            catch (ManagementException ex)
            {
                Log.Instance.Trace($"GetBiosSelectionsAsync failed for [{settingName}].", ex);
                return [];
            }
        }

        public static async Task<string> GetBiosSettingAsync(string settingName)
        {
            try
            {
                var results = await ReadAsync(
                    "root\\WMI",
                    $"SELECT * FROM Lenovo_BiosSetting",
                    pdc => pdc["CurrentSetting"]?.Value?.ToString()
                ).ConfigureAwait(false);

                foreach (var current in results)
                {
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
            }
            catch (ManagementException ex)
            {
                Log.Instance.Trace($"GetBiosSettingAsync failed for [{settingName}].", ex);
                return string.Empty;
            }

            Log.Instance.Trace($"GetBiosSettingAsync result for [{settingName}]: Empty/Not Found");
            return string.Empty;
        }

        public static async Task SetBiosSettingAsync(string settingName, string value)
        {
            Log.Instance.Trace($"SetBiosSettingAsync executing for [{settingName}] with value [{value}]");

            await CallAsync(
                "root\\WMI",
                $"SELECT * FROM Lenovo_SetBiosSetting",
                "SetBiosSetting",
                new() { { "parameter", $"{settingName},{value}," } }).ConfigureAwait(false);
        }

        public static async Task SaveBiosSettingAsync()
        {
            Log.Instance.Trace($"SaveBiosSettingAsync executing");

            await CallAsync(
                "root\\WMI",
                $"SELECT * FROM Lenovo_SaveBiosSettings",
                "SaveBiosSettings",
                new()).ConfigureAwait(false);
        }
    }
}
