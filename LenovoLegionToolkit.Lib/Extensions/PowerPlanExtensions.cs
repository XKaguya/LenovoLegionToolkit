using System;
using System.Runtime.InteropServices;

namespace LenovoLegionToolkit.Lib.Extensions;

public static class PowerPlanExtensions
{
    private static readonly Guid BalancedBaseGuid = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid NoSubGroupGuid = new("fea3413e-7e05-4911-9a71-700331f1c294");
    private static readonly Guid PersonalityGuid = new("245d8541-3943-4422-b025-13a784f679b7");

    private const uint PersonalityBalanced = 2;

    [DllImport("PowrProf.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("PowrProf.dll")]
    private static extern uint PowerReadACValueIndex(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        ref Guid subGroupOfPowerSettingsGuid,
        ref Guid powerSettingGuid,
        out uint valueIndex);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static bool IsPlanBasedOnBalanced(Guid schemeGuid)
    {
        var subGroup = NoSubGroupGuid;
        var personalitySetting = PersonalityGuid;
        var result = PowerReadACValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref personalitySetting, out var personality);
        if (result == 0)
            return personality == PersonalityBalanced;

        return schemeGuid == BalancedBaseGuid;
    }

    public static bool IsActivePlanBasedOnBalanced()
    {
        var result = PowerGetActiveScheme(IntPtr.Zero, out var activePolicyGuidPtr);
        if (result != 0)
            return false;

        var activePolicyGuid = Marshal.PtrToStructure<Guid>(activePolicyGuidPtr);
        LocalFree(activePolicyGuidPtr);

        return IsPlanBasedOnBalanced(activePolicyGuid);
    }
}
