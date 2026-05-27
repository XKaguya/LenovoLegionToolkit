using System;
using System.Collections.Generic;

namespace LenovoLegionToolkit.Lib.Extensions;

public static class PowerOverrideExtensions
{
    public static T? TryGetEnum<T>(this Dictionary<PowerOverrideKey, string>? overrides, PowerOverrideKey key)
        where T : struct, Enum
    {
        if (overrides != null && overrides.TryGetValue(key, out var val) && Enum.TryParse(val, out T result))
            return result;
        return null;
    }

    public static Guid? TryGetGuid(this Dictionary<PowerOverrideKey, string>? overrides, PowerOverrideKey key)
    {
        if (overrides != null && overrides.TryGetValue(key, out var val) && Guid.TryParse(val, out var result))
            return result;
        return null;
    }

    public static void SetEnum<T>(this Dictionary<PowerOverrideKey, string> overrides, PowerOverrideKey key, T? value)
        where T : struct, Enum
    {
        if (value.HasValue)
            overrides[key] = value.Value.ToString();
        else
            overrides.Remove(key);
    }

    public static void SetGuid(this Dictionary<PowerOverrideKey, string> overrides, PowerOverrideKey key, Guid? value)
    {
        if (value.HasValue)
            overrides[key] = value.Value.ToString();
        else
            overrides.Remove(key);
    }

    public static WindowsPowerMode? GetPowerModeOnAc(this Dictionary<PowerModeState, Dictionary<PowerOverrideKey, string>> overrides, PowerModeState state) =>
        overrides.TryGetValue(state, out var dict) ? dict.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerModeOnAc) : null;

    public static void SetPowerModeOnAc(this Dictionary<PowerModeState, Dictionary<PowerOverrideKey, string>> overrides, PowerModeState state, WindowsPowerMode? mode)
    {
        if (!overrides.TryGetValue(state, out var dict))
            overrides[state] = dict = [];
        dict.SetEnum(PowerOverrideKey.PowerModeOnAc, mode);
        if (dict.Count == 0)
            overrides.Remove(state);
    }

    public static WindowsPowerMode? GetPowerModeOnDc(this Dictionary<PowerModeState, Dictionary<PowerOverrideKey, string>> overrides, PowerModeState state) =>
        overrides.TryGetValue(state, out var dict) ? dict.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerModeOnDc) : null;

    public static void SetPowerModeOnDc(this Dictionary<PowerModeState, Dictionary<PowerOverrideKey, string>> overrides, PowerModeState state, WindowsPowerMode? mode)
    {
        if (!overrides.TryGetValue(state, out var dict))
            overrides[state] = dict = [];
        dict.SetEnum(PowerOverrideKey.PowerModeOnDc, mode);
        if (dict.Count == 0)
            overrides.Remove(state);
    }

    public static WindowsPowerMode? GetPowerPlanBalanceOnAc(this Dictionary<PowerModeState, Dictionary<PowerOverrideKey, string>> overrides, PowerModeState state) =>
        overrides.TryGetValue(state, out var dict) ? dict.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerPlanBalanceOnAc) : null;

    public static void SetPowerPlanBalanceOnAc(this Dictionary<PowerModeState, Dictionary<PowerOverrideKey, string>> overrides, PowerModeState state, WindowsPowerMode? mode)
    {
        if (!overrides.TryGetValue(state, out var dict))
            overrides[state] = dict = [];
        dict.SetEnum(PowerOverrideKey.PowerPlanBalanceOnAc, mode);
        if (dict.Count == 0)
            overrides.Remove(state);
    }

    public static WindowsPowerMode? GetPowerPlanBalanceOnDc(this Dictionary<PowerModeState, Dictionary<PowerOverrideKey, string>> overrides, PowerModeState state) =>
        overrides.TryGetValue(state, out var dict) ? dict.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerPlanBalanceOnDc) : null;

    public static void SetPowerPlanBalanceOnDc(this Dictionary<PowerModeState, Dictionary<PowerOverrideKey, string>> overrides, PowerModeState state, WindowsPowerMode? mode)
    {
        if (!overrides.TryGetValue(state, out var dict))
            overrides[state] = dict = [];
        dict.SetEnum(PowerOverrideKey.PowerPlanBalanceOnDc, mode);
        if (dict.Count == 0)
            overrides.Remove(state);
    }

    public static WindowsPowerMode? GetPowerModeOnAc(this Dictionary<ITSMode, Dictionary<PowerOverrideKey, string>> itsOverrides, ITSMode mode) =>
        itsOverrides.TryGetValue(mode, out var dict) ? dict.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerModeOnAc) : null;

    public static void SetPowerModeOnAc(this Dictionary<ITSMode, Dictionary<PowerOverrideKey, string>> itsOverrides, ITSMode mode, WindowsPowerMode? modeValue)
    {
        if (!itsOverrides.TryGetValue(mode, out var dict))
            itsOverrides[mode] = dict = [];
        dict.SetEnum(PowerOverrideKey.PowerModeOnAc, modeValue);
        if (dict.Count == 0)
            itsOverrides.Remove(mode);
    }

    public static WindowsPowerMode? GetPowerModeOnDc(this Dictionary<ITSMode, Dictionary<PowerOverrideKey, string>> itsOverrides, ITSMode mode) =>
        itsOverrides.TryGetValue(mode, out var dict) ? dict.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerModeOnDc) : null;

    public static void SetPowerModeOnDc(this Dictionary<ITSMode, Dictionary<PowerOverrideKey, string>> itsOverrides, ITSMode mode, WindowsPowerMode? modeValue)
    {
        if (!itsOverrides.TryGetValue(mode, out var dict))
            itsOverrides[mode] = dict = [];
        dict.SetEnum(PowerOverrideKey.PowerModeOnDc, modeValue);
        if (dict.Count == 0)
            itsOverrides.Remove(mode);
    }

    public static WindowsPowerMode? GetPowerPlanBalanceOnAc(this Dictionary<ITSMode, Dictionary<PowerOverrideKey, string>> itsOverrides, ITSMode mode) =>
        itsOverrides.TryGetValue(mode, out var dict) ? dict.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerPlanBalanceOnAc) : null;

    public static void SetPowerPlanBalanceOnAc(this Dictionary<ITSMode, Dictionary<PowerOverrideKey, string>> itsOverrides, ITSMode mode, WindowsPowerMode? modeValue)
    {
        if (!itsOverrides.TryGetValue(mode, out var dict))
            itsOverrides[mode] = dict = [];
        dict.SetEnum(PowerOverrideKey.PowerPlanBalanceOnAc, modeValue);
        if (dict.Count == 0)
            itsOverrides.Remove(mode);
    }

    public static WindowsPowerMode? GetPowerPlanBalanceOnDc(this Dictionary<ITSMode, Dictionary<PowerOverrideKey, string>> itsOverrides, ITSMode mode) =>
        itsOverrides.TryGetValue(mode, out var dict) ? dict.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerPlanBalanceOnDc) : null;

    public static void SetPowerPlanBalanceOnDc(this Dictionary<ITSMode, Dictionary<PowerOverrideKey, string>> itsOverrides, ITSMode mode, WindowsPowerMode? modeValue)
    {
        if (!itsOverrides.TryGetValue(mode, out var dict))
            itsOverrides[mode] = dict = [];
        dict.SetEnum(PowerOverrideKey.PowerPlanBalanceOnDc, modeValue);
        if (dict.Count == 0)
            itsOverrides.Remove(mode);
    }
}
