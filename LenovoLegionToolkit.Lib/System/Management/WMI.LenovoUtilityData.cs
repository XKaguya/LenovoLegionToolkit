using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// ReSharper disable InconsistentNaming
// ReSharper disable StringLiteralTypo

namespace LenovoLegionToolkit.Lib.System.Management;

public static partial class WMI
{
    public static class LenovoUtilityData
    {
        public static Task SetFeatureAsync(SpecialKeyLedState ledState) => WMI.CallAsync("root\\WMI",
              $"SELECT * FROM LENOVO_UTILITY_DATA",
              "SetFeature",
              new() { { "featuretype", ledState } });
    }
}
