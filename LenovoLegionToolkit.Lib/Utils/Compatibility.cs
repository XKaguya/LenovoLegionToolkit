using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.System.Management;
using RAMSPDToolkit.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;

// ReSharper disable StringLiteralTypo

namespace LenovoLegionToolkit.Lib.Utils;

public static partial class Compatibility
{
    [GeneratedRegex("^[A-Z0-9]{4}")]
    private static partial Regex BiosPrefixRegex();

    [GeneratedRegex("[0-9]{2}")]
    private static partial Regex BiosVersionRegex();

    private const string ALLOWED_VENDOR = "LENOVO";

    private static readonly string[] AllowedModelsPrefix = [
        // Legion Go
        "8APU1",

        // Worldwide variants
        "17ACH",
        "17ARH",
        "17ITH",
        "17IMH",

        "16ACH",
        "16AFR",
        "16AHP",
        "16APH",
        "16ARH",
        "16ARP",
        "16ARX",
        "16IAH",
        "16IAX",
        "16IMH",
        "16IRH",
        "16IRX",
        "16ITH",

        "18IAX",
        "NX",

        "15ACH",
        "15AHP",
        "15AKP",
        "15APH",
        "15ARH",
        "15ARP",
        "15IAH",
        "15IAX",
        "15IHU",
        "15IMH",
        "15IRH",
        "15IRX",
        "15ITH",

        "14APH",
        "14IRP",

        // Chinese variants
        "G5000",
        "R9000",
        "R7000",
        "Y9000",
        "Y7000",
            
        // Limited compatibility
        "17IR",
        "15IR",
        "15IC",
        "15IK"
    ];

    private static MachineInformation? _machineInformation;

    public static Task<bool> CheckBasicCompatibilityAsync() => WMI.LenovoGameZoneData.ExistsAsync();

    public static async Task<(bool isCompatible, MachineInformation machineInformation)> IsCompatibleAsync()
    {
        var mi = await GetMachineInformationAsync().ConfigureAwait(false);

        if (!await CheckBasicCompatibilityAsync().ConfigureAwait(false))
            return (false, mi);

        if (!mi.Vendor.Equals(ALLOWED_VENDOR, StringComparison.InvariantCultureIgnoreCase))
            return (false, mi);

        foreach (var allowedModel in AllowedModelsPrefix)
            if (mi.Model.Contains(allowedModel, StringComparison.InvariantCultureIgnoreCase))
                return (true, mi);

        return (false, mi);
    }

    public static async Task<MachineInformation> GetMachineInformationAsync()
    {
        if (_machineInformation.HasValue)
            return _machineInformation.Value;

        var (vendor, machineType, model, serialNumber) = await GetModelDataAsync().ConfigureAwait(false);
        var generation = GetMachineGeneration(model);
        var legionSeries = GetLegionSeries(model, machineType);
        var (biosVersion, biosVersionRaw) = GetBIOSVersion();
        var supportedPowerModes = (await GetSupportedPowerModesAsync().ConfigureAwait(false)).ToArray();
        var smartFanVersion = await GetSmartFanVersionAsync().ConfigureAwait(false);
        var legionZoneVersion = await GetLegionZoneVersionAsync().ConfigureAwait(false);
        var features = await GetFeaturesAsync().ConfigureAwait(false);

        var machineInformation = new MachineInformation
        {
            Generation = generation,
            LegionSeries = legionSeries,
            Vendor = vendor,
            MachineType = machineType,
            Model = model,
            SerialNumber = serialNumber,
            BiosVersion = biosVersion,
            BiosVersionRaw = biosVersionRaw,
            SupportedPowerModes = supportedPowerModes,
            SmartFanVersion = smartFanVersion,
            LegionZoneVersion = legionZoneVersion,
            Features = features,
            Properties = new()
            {
                SupportsAlwaysOnAc = GetAlwaysOnAcStatus(),
                SupportsExtremeMode = GetSupportsExtremeMode(supportedPowerModes, smartFanVersion, legionZoneVersion),
                SupportsGodModeV1 = GetSupportsGodModeV1(supportedPowerModes, smartFanVersion, legionZoneVersion, biosVersion),
                SupportsGodModeV2 = GetSupportsGodModeV2(supportedPowerModes, smartFanVersion, legionZoneVersion),
                SupportsGodModeV3 = GetSupportsGodModeV3(supportedPowerModes, smartFanVersion, legionZoneVersion, generation, model),
                SupportsGodModeV4 = GetSupportsGodModeV4(supportedPowerModes, smartFanVersion, legionZoneVersion),
                SupportsGSync = await GetSupportsGSyncAsync().ConfigureAwait(false),
                SupportsIGPUMode = await GetSupportsIGPUModeAsync().ConfigureAwait(false),
                SupportsAIMode = await GetSupportsAIModeAsync().ConfigureAwait(false),
                SupportBootLogoChange = GetSupportBootLogoChange(smartFanVersion),
                SupportITSMode = GetSupportITSMode(model),
                HasQuietToPerformanceModeSwitchingBug = GetHasQuietToPerformanceModeSwitchingBug(biosVersion),
                HasGodModeToOtherModeSwitchingBug = GetHasGodModeToOtherModeSwitchingBug(biosVersion),
                HasReapplyParameterIssue = GetHasReapplyParameterIssue(model),
                HasSpectrumProfileSwitchingBug = GetHasSpectrumProfileSwitchingBug(model),
                IsExcludedFromLenovoLighting = GetIsExcludedFromLenovoLighting(biosVersion),
                IsExcludedFromPanelLogoLenovoLighting = GetIsExcludedFromPanelLenovoLighting(machineType, model),
                HasAlternativeFullSpectrumLayout = GetHasAlternativeFullSpectrumLayout(machineType),
            }
        };

        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Retrieved machine information:");
            Log.Instance.Trace($" * Generation: '{machineInformation.Generation}'");
            Log.Instance.Trace($" * Legion Series: '{machineInformation.LegionSeries}'");
            Log.Instance.Trace($" * Vendor: '{machineInformation.Vendor}'");
            Log.Instance.Trace($" * Machine Type: '{machineInformation.MachineType}'");
            Log.Instance.Trace($" * Model: '{machineInformation.Model}'");
            Log.Instance.Trace($" * BIOS: '{machineInformation.BiosVersion}' [{machineInformation.BiosVersionRaw}]");
            Log.Instance.Trace($" * SupportedPowerModes: '{string.Join(",", machineInformation.SupportedPowerModes)}'");
            Log.Instance.Trace($" * SmartFanVersion: '{machineInformation.SmartFanVersion}'");
            Log.Instance.Trace($" * LegionZoneVersion: '{machineInformation.LegionZoneVersion}'");
            Log.Instance.Trace($" * Features: {machineInformation.Features.Source}:{string.Join(',', machineInformation.Features.All)}");
            Log.Instance.Trace($" * Properties:");
            Log.Instance.Trace($"     * SupportsExtremeMode: '{machineInformation.Properties.SupportsExtremeMode}'");
            Log.Instance.Trace($"     * SupportsAlwaysOnAc: '{machineInformation.Properties.SupportsAlwaysOnAc.status}, {machineInformation.Properties.SupportsAlwaysOnAc.connectivity}'");
            Log.Instance.Trace($"     * SupportsGodModeV1: '{machineInformation.Properties.SupportsGodModeV1}'");
            Log.Instance.Trace($"     * SupportsGodModeV2: '{machineInformation.Properties.SupportsGodModeV2}'");
            Log.Instance.Trace($"     * SupportsGodModeV3: '{machineInformation.Properties.SupportsGodModeV3}'");
            Log.Instance.Trace($"     * SupportsGodModeV4: '{machineInformation.Properties.SupportsGodModeV4}'");
            Log.Instance.Trace($"     * SupportsGSync: '{machineInformation.Properties.SupportsGSync}'");
            Log.Instance.Trace($"     * SupportsIGPUMode: '{machineInformation.Properties.SupportsIGPUMode}'");
            Log.Instance.Trace($"     * SupportsAIMode: '{machineInformation.Properties.SupportsAIMode}'");
            Log.Instance.Trace($"     * SupportBootLogoChange: '{machineInformation.Properties.SupportBootLogoChange}'");
            Log.Instance.Trace($"     * HasQuietToPerformanceModeSwitchingBug: '{machineInformation.Properties.HasQuietToPerformanceModeSwitchingBug}'");
            Log.Instance.Trace($"     * HasGodModeToOtherModeSwitchingBug: '{machineInformation.Properties.HasGodModeToOtherModeSwitchingBug}'");
            Log.Instance.Trace($"     * HasReapplyParameterIssue: '{machineInformation.Properties.HasReapplyParameterIssue}'");
            Log.Instance.Trace($"     * HasSpectrumProfileSwitchingBug: '{machineInformation.Properties.HasSpectrumProfileSwitchingBug}'");
            Log.Instance.Trace($"     * IsExcludedFromLenovoLighting: '{machineInformation.Properties.IsExcludedFromLenovoLighting}'");
            Log.Instance.Trace($"     * IsExcludedFromPanelLogoLenovoLighting: '{machineInformation.Properties.IsExcludedFromPanelLogoLenovoLighting}'");
            Log.Instance.Trace($"     * HasAlternativeFullSpectrumLayout: '{machineInformation.Properties.HasAlternativeFullSpectrumLayout}'");
        }

        return (_machineInformation = machineInformation).Value;
    }


    private static Task<(string, string, string, string)> GetModelDataAsync() => WMI.Win32.ComputerSystemProduct.ReadAsync();

    private static (BiosVersion?, string?) GetBIOSVersion()
    {
        var result = Registry.GetValue("HKEY_LOCAL_MACHINE", "HARDWARE\\DESCRIPTION\\System\\BIOS", "BIOSVersion", string.Empty).Trim();

        var prefixRegex = BiosPrefixRegex();
        var versionRegex = BiosVersionRegex();

        var prefix = prefixRegex.Match(result).Value;
        var versionString = versionRegex.Match(result).Value;

        if (!int.TryParse(versionRegex.Match(versionString).Value, out var version))
            return (null, null);

        return (new(prefix, version), result);
    }

    private static async Task<MachineInformation.FeatureData> GetFeaturesAsync()
    {
        try
        {
            var capabilities = await WMI.LenovoCapabilityData00.ReadAsync().ConfigureAwait(false);
            return new(MachineInformation.FeatureData.SourceType.CapabilityData, capabilities);
        }
        catch { /* Ignored. */ }

        try
        {
            var featureFlags = await WMI.LenovoOtherMethod.GetLegionDeviceSupportFeatureAsync().ConfigureAwait(false);

            return new(MachineInformation.FeatureData.SourceType.Flags)
            {
                [CapabilityID.IGPUMode] = featureFlags.IsBitSet(0),
                [CapabilityID.NvidiaGPUDynamicDisplaySwitching] = featureFlags.IsBitSet(4),
                [CapabilityID.InstantBootAc] = featureFlags.IsBitSet(5),
                [CapabilityID.InstantBootUsbPowerDelivery] = featureFlags.IsBitSet(6),
                [CapabilityID.AMDSmartShiftMode] = featureFlags.IsBitSet(7),
                [CapabilityID.AMDSkinTemperatureTracking] = featureFlags.IsBitSet(8),
                [CapabilityID.FlipToStart] = true,
                [CapabilityID.OverDrive] = true
            };
        }
        catch { /* Ignored. */ }

        return MachineInformation.FeatureData.Unknown;
    }

    private static async Task<IEnumerable<PowerModeState>> GetSupportedPowerModesAsync()
    {
        try
        {
            var powerModes = new List<PowerModeState>();

            var value = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.SupportedPowerModes).ConfigureAwait(false);

            if (value.IsBitSet(0))
                powerModes.Add(PowerModeState.Quiet);
            if (value.IsBitSet(1))
                powerModes.Add(PowerModeState.Balance);
            if (value.IsBitSet(2))
                powerModes.Add(PowerModeState.Performance);
            if (value.IsBitSet(16))
            {
                powerModes.Add(PowerModeState.Extreme);
                powerModes.Add(PowerModeState.GodMode);
            }

            return powerModes;
        }
        catch { /* Ignored. */ }

        try
        {
            var powerModes = new List<PowerModeState>();

            var result = await WMI.LenovoOtherMethod.GetSupportThermalModeAsync().ConfigureAwait(false);

            if (result.IsBitSet(0))
                powerModes.Add(PowerModeState.Quiet);
            if (result.IsBitSet(1))
                powerModes.Add(PowerModeState.Balance);
            if (result.IsBitSet(2))
                powerModes.Add(PowerModeState.Performance);
            if (result.IsBitSet(16))
            {
                powerModes.Add(PowerModeState.Extreme);
                powerModes.Add(PowerModeState.GodMode);
            }

            return powerModes;
        }
        catch { /* Ignored. */ }

        return [];
    }

    private static async Task<int> GetSmartFanVersionAsync()
    {
        try
        {
            return await WMI.LenovoGameZoneData.IsSupportSmartFanAsync().ConfigureAwait(false);
        }
        catch { /* Ignored. */ }

        return -1;
    }

    private static async Task<int> GetLegionZoneVersionAsync()
    {
        try
        {
            return await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.LegionZoneSupportVersion).ConfigureAwait(false);
        }
        catch { /* Ignored. */ }

        try
        {
            return await WMI.LenovoOtherMethod.GetSupportLegionZoneVersionAsync().ConfigureAwait(false);
        }
        catch { /* Ignored. */ }

        return -1;
    }

    private static unsafe (bool status, bool connectivity) GetAlwaysOnAcStatus()
    {
        var capabilities = new SYSTEM_POWER_CAPABILITIES();
        var result = PInvoke.CallNtPowerInformation(POWER_INFORMATION_LEVEL.SystemPowerCapabilities,
            null,
            0,
            &capabilities,
            (uint)Marshal.SizeOf<SYSTEM_POWER_CAPABILITIES>());

        if (result.SeverityCode == NTSTATUS.Severity.Success)
            return (false, false);

        return (capabilities.AoAc, capabilities.AoAcConnectivitySupported);
    }

    private static bool GetSupportsExtremeMode(IEnumerable<PowerModeState> supportedPowerModes, int smartFanVersion, int legionZoneVersion)
    {
        if (!supportedPowerModes.Contains(PowerModeState.Extreme))
            return false;

        return smartFanVersion is 7 or 8 || legionZoneVersion is 4 or 5;
    }

    private static bool GetSupportsGodModeV1(IEnumerable<PowerModeState> supportedPowerModes, int smartFanVersion, int legionZoneVersion, BiosVersion? biosVersion)
    {
        if (!supportedPowerModes.Contains(PowerModeState.GodMode))
            return false;

        var affectedBiosVersions = new BiosVersion[]
        {
            new("G9CN", 24),
            new("GKCN", 46),
            new("H1CN", 39),
            new("HACN", 31),
            new("HHCN", 20)
        };

        if (affectedBiosVersions.Any(bv => biosVersion?.IsLowerThan(bv) ?? false))
            return false;

        return smartFanVersion is 4 or 5 || legionZoneVersion is 1 or 2;
    }

    private static bool GetSupportsGodModeV2(IEnumerable<PowerModeState> supportedPowerModes, int smartFanVersion, int legionZoneVersion)
    {
        if (!supportedPowerModes.Contains(PowerModeState.GodMode))
            return false;

        return smartFanVersion is 6 or 7 || legionZoneVersion is 3 or 4;
    }

    private static bool GetSupportsGodModeV3(IEnumerable<PowerModeState> supportedPowerModes, int smartFanVersion, int legionZoneVersion, int gen, string model)
    {
        if (!supportedPowerModes.Contains(PowerModeState.GodMode))
        {
            return false;
        }

        var affectedSeries = new LegionSeries[]
        {
            LegionSeries.Legion_5,
            LegionSeries.Legion_7
        };

        var affectedModels = new string[]
        {
            "Legion 5", // Y7000P
            "Legion 7", // Y9000X, Not Y9000P.
            "Legion Pro 5 16IAX10H", // Y7000P With RTX 5070TI
            "LOQ",
            "Y7000" 
        };

        var (_, type, _, _) = GetModelDataAsync().Result;
        var isAffectedSeries = affectedSeries.Any(m => GetLegionSeries(model, type) == m);
        var isAffectedModel = affectedModels.Any(m => model.Contains(m));
        var isSupportedVersion = smartFanVersion is 8 or 9 || legionZoneVersion is 5 or 6;

        return (isAffectedSeries || isAffectedModel) && isSupportedVersion && gen >= 10;
    }

    private static bool GetSupportsGodModeV4(IEnumerable<PowerModeState> supportedPowerModes, int smartFanVersion, int legionZoneVersion)
    {
        if (!supportedPowerModes.Contains(PowerModeState.GodMode))
            return false;

        // In theory, All models that has denied by GetSupportsGodModeV3() will be supported by GodModeControllerV4.

        return smartFanVersion is 8 or 9 || legionZoneVersion is 5 or 6;
    }


    private static async Task<bool> GetSupportsGSyncAsync()
    {
        try
        {
            return await WMI.LenovoGameZoneData.IsSupportGSyncAsync().ConfigureAwait(false) > 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> GetSupportsIGPUModeAsync()
    {
        try
        {
            return await WMI.LenovoGameZoneData.IsSupportIGPUModeAsync().ConfigureAwait(false) > 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> GetSupportsAIModeAsync()
    {
        try
        {
            await WMI.LenovoGameZoneData.GetIntelligentSubModeAsync().ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool GetSupportBootLogoChange(int smartFanVersion)
    {
        // I don't know why. Which means every model should support Boot Logo Change.
        // return smartFanVersion < 9;
        return true;
    }

    private static bool GetSupportITSMode(string model)
    {
        var lower = model.ToLowerInvariant();
        return lower.Contains("IdeaPad".ToLowerInvariant()) || lower.Contains("Lenovo Slim".ToLowerInvariant()); // || lower.Contains("YOGA".ToLowerInvariant());
                                                                                                                 // Comment this line due to YOGA does not support ITS Mode from user's report.
    }

    private static int GetMachineGeneration(string model)
    {
        Match match = Regex.Match(model, @"\d+(?=[A-Z]?H?$)");

        if (match.Success)
        {
            return Int32.Parse(match.Value);
        }
        else
        {
            return 0;
        }
    }

    private static LegionSeries GetLegionSeries(string model, string machineType)
    {
        LegionSeries seriesByMachineType = machineType switch
        {
            "83F0" or "83F1" or "83M0" or "83NX" or "83N2" or "83LY" or "83DG" or "83EW" or "83EG" or "83JJ" or "82RC" or "82RB" or "82TB" or "83EF" or "82RE" or "82RD" => LegionSeries.Legion_5,

            "83DH" or "83EX" or "82Y5" or "82Y9" or "82YA" or "83D6" => LegionSeries.Legion_Slim_5,

            "83LT" or "83F3" or "83DF" or "83F2" or "83LU" or "82WM" or "83NN" or "82WK" => LegionSeries.Legion_Pro_5,

            "83KY" or "83FD" or "82UH" or "82TD" => LegionSeries.Legion_7,

            "83RU" or "83F5" or "83DE" or "82WR" or "82WQ" or "82WS" => LegionSeries.Legion_Pro_7,

            "83G0" or "83EY" => LegionSeries.Legion_9,

            "83E1" => LegionSeries.Legion_Go,

            _ => LegionSeries.Unknown
        };

        if (seriesByMachineType != LegionSeries.Unknown)
        {
            return seriesByMachineType;
        }

        if (model.ToLowerInvariant().Contains("LOQ".ToLowerInvariant()))
        {
            return LegionSeries.LOQ;
        }
        else if (model.ToLowerInvariant().Contains("IdeaPad".ToLowerInvariant()))
        {
            return LegionSeries.IdeaPad;
        }
        else if (model.ToLowerInvariant().Contains(("YOGA").ToLowerInvariant()))
        {
            return LegionSeries.YOGA;
        }
        else if (model.ToLowerInvariant().Contains("Lenovo Slim".ToLowerInvariant()))
        {
            return LegionSeries.Lenovo_Slim;
        }

        return LegionSeries.Unknown;
    }

    private static bool GetHasQuietToPerformanceModeSwitchingBug(BiosVersion? biosVersion)
    {
        var affectedBiosVersions = new BiosVersion[]
        {
            new("J2CN", null)
        };

        return affectedBiosVersions.Any(bv => biosVersion?.IsHigherOrEqualThan(bv) ?? false);
    }

    private static bool GetHasGodModeToOtherModeSwitchingBug(BiosVersion? biosVersion)
    {
        var affectedBiosVersions = new BiosVersion[]
        {
            new("K1CN", null)
        };

        return affectedBiosVersions.Any(bv => biosVersion?.IsHigherOrEqualThan(bv) ?? false);
    }

    private static bool GetHasReapplyParameterIssue(string? machineModel)
    {
        if (string.IsNullOrEmpty(machineModel))
        {
            return false;
        }

        var affectedSeries = new LegionSeries[]
        {
            LegionSeries.Legion_5,
            LegionSeries.Legion_7,
            LegionSeries.Legion_9,
        };

        var (_, type, _, _) = GetModelDataAsync().Result;
        if (type == null)
        {
            return false;
        }

        return affectedSeries.Any(model =>GetLegionSeries(machineModel, type) == model);
    }

    private static bool GetHasSpectrumProfileSwitchingBug(string? machineModel)
    {
        if (string.IsNullOrEmpty(machineModel))
        {
            return false;
        }

        var affectedSeries = new LegionSeries[]
        {
            LegionSeries.Legion_5,
        };

        var affectedModel = new List<string>
        {
            "15IRX10",
            "15AHP10"
        };

        var(_, type, _, _) = GetModelDataAsync().Result;
        if (type == null)
        {
            return false;
        }

        bool isAffectedModel = affectedModel.Any(model =>machineModel.Contains(model, StringComparison.OrdinalIgnoreCase));
        bool isAffectedSeries = affectedSeries.Any(model =>GetLegionSeries(machineModel, type) == model);

        return isAffectedModel && isAffectedSeries;
    }

    private static bool GetIsExcludedFromLenovoLighting(BiosVersion? biosVersion)
    {
        var affectedBiosVersions = new BiosVersion[]
        {
            new("GKCN", 54)
        };

        return affectedBiosVersions.Any(bv => biosVersion?.IsLowerThan(bv) ?? false);
    }

    private static bool GetIsExcludedFromPanelLenovoLighting(string machineType, string model)
    {
        (string machineType, string model)[] excludedModels =
        [
            ("82JH", "15ITH6H"),
            ("82JK", "15ITH6"),
            ("82JM", "17ITH6H"),
            ("82JN", "17ITH6"),
            ("82JU", "15ACH6H"),
            ("82JW", "15ACH6"),
            ("82JY", "17ACH6H"),
            ("82K0", "17ACH6"),
            ("82K1", "15IHU6"),
            ("82K2", "15ACH6"),
            ("82NW", "15ACH6A")
        ];

        return excludedModels.Where(m =>
        {
            var result = machineType.Contains(m.machineType);
            result &= model.Contains(m.model);
            return result;
        }).Any();
    }

    private static bool GetHasAlternativeFullSpectrumLayout(string machineType)
    {
        var machineTypes = new[]
        {
            "83G0", // Gen 9
            "83AG"  // Gen 8
        };
        return machineTypes.Contains(machineType);
    }

    public static void PrintMachineInfo()
    {
        if (Log.Instance.IsTraceEnabled)
        {
            if (_machineInformation == null)
            {
                Log.Instance.Trace($"Machine information is not retrieved yet.");
                return;
            }

            Log.Instance.Trace($"Retrieved machine information:");
            Log.Instance.Trace($" * Generation: '{_machineInformation.Value.Generation}'");
            Log.Instance.Trace($" * Legion Series: '{_machineInformation.Value.LegionSeries}'");
            Log.Instance.Trace($" * Vendor: '{_machineInformation.Value.Vendor}'");
            Log.Instance.Trace($" * Machine Type: '{_machineInformation.Value.MachineType}'");
            Log.Instance.Trace($" * Model: '{_machineInformation.Value.Model}'");
            Log.Instance.Trace($" * BIOS: '{_machineInformation.Value.BiosVersion}' [{_machineInformation.Value.BiosVersionRaw}]");
            Log.Instance.Trace($" * SupportedPowerModes: '{string.Join(",", _machineInformation.Value.SupportedPowerModes)}'");
            Log.Instance.Trace($" * SmartFanVersion: '{_machineInformation.Value.SmartFanVersion}'");
            Log.Instance.Trace($" * LegionZoneVersion: '{_machineInformation.Value.LegionZoneVersion}'");
            Log.Instance.Trace($" * Features: {_machineInformation.Value.Features.Source}:{string.Join(',', _machineInformation.Value.Features.All)}");
            Log.Instance.Trace($" * Properties:");
            Log.Instance.Trace($"     * SupportsExtremeMode: '{_machineInformation.Value.Properties.SupportsExtremeMode}'");
            Log.Instance.Trace($"     * SupportsAlwaysOnAc: '{_machineInformation.Value.Properties.SupportsAlwaysOnAc.status}, {_machineInformation.Value.Properties.SupportsAlwaysOnAc.connectivity}'");
            Log.Instance.Trace($"     * SupportsGodModeV1: '{_machineInformation.Value.Properties.SupportsGodModeV1}'");
            Log.Instance.Trace($"     * SupportsGodModeV2: '{_machineInformation.Value.Properties.SupportsGodModeV2}'");
            Log.Instance.Trace($"     * SupportsGodModeV3: '{_machineInformation.Value.Properties.SupportsGodModeV3}'");
            Log.Instance.Trace($"     * SupportsGodModeV4: '{_machineInformation.Value.Properties.SupportsGodModeV4}'");
            Log.Instance.Trace($"     * SupportsGSync: '{_machineInformation.Value.Properties.SupportsGSync}'");
            Log.Instance.Trace($"     * SupportsIGPUMode: '{_machineInformation.Value.Properties.SupportsIGPUMode}'");
            Log.Instance.Trace($"     * SupportsAIMode: '{_machineInformation.Value.Properties.SupportsAIMode}'");
            Log.Instance.Trace($"     * SupportBootLogoChange: '{_machineInformation.Value.Properties.SupportBootLogoChange}'");
            Log.Instance.Trace($"     * HasQuietToPerformanceModeSwitchingBug: '{_machineInformation.Value.Properties.HasQuietToPerformanceModeSwitchingBug}'");
            Log.Instance.Trace($"     * HasGodModeToOtherModeSwitchingBug: '{_machineInformation.Value.Properties.HasGodModeToOtherModeSwitchingBug}'");
            Log.Instance.Trace($"     * HasReapplyParameterIssue: '{_machineInformation.Value.Properties.HasReapplyParameterIssue}'");
            Log.Instance.Trace($"     * HasSpectrumProfileSwitchingBug: '{_machineInformation.Value.Properties.HasSpectrumProfileSwitchingBug}'");
            Log.Instance.Trace($"     * IsExcludedFromLenovoLighting: '{_machineInformation.Value.Properties.IsExcludedFromLenovoLighting}'");
            Log.Instance.Trace($"     * IsExcludedFromPanelLogoLenovoLighting: '{_machineInformation.Value.Properties.IsExcludedFromPanelLogoLenovoLighting}'");
            Log.Instance.Trace($"     * HasAlternativeFullSpectrumLayout: '{_machineInformation.Value.Properties.HasAlternativeFullSpectrumLayout}'");
        }
    }

    public static void PrintControllerVersion()
    {
        if (Log.Instance.IsTraceEnabled)
        {
            SensorsController? sensorsController = IoCContainer.Resolve<SensorsController>();
            var sensorsControllerTypeName = sensorsController?.GetControllerAsync().Result?.GetType().Name ?? "Null SensorsController or Result";
            Log.Instance.Trace($"Using {sensorsControllerTypeName}");


            GodModeController? godModeController = IoCContainer.Resolve<GodModeController>();
            var godModeControllerTypeName = godModeController?.Controller?.GetType().Name ?? "Null";
            Log.Instance.Trace($"Using {godModeControllerTypeName}");
        }
    }
}
