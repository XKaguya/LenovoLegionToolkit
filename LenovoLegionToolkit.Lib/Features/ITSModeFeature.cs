using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Features;

public partial class ITSModeFeature : IFeature<ITSMode>
{
    #region Constants and Imports
    private const uint ITS_VERSION_3 = 16384U;
    private const uint ITS_VERSION_4 = 20480U;
    private const uint ITS_VERSION_5 = 24576U;
    private const uint DISPATCHER_VERSION_2 = 4096U;
    private const uint DISPATCHER_VERSION_3 = 8192U;
    private const uint DISPATCHER_VERSION_4 = 12288U;

    [LibraryImport("PowerBattery.dll", EntryPoint = "?SetITSMode@CIntelligentCooling@PowerBattery@@QEAAHAEAW4ITSMode@12@@Z", SetLastError = true)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int SetITSMode(ref CIntelligentCooling instance, ref ITSMode itsMode);

    [LibraryImport("PowerBattery.dll", EntryPoint = "?GetITSMode@CIntelligentCooling@PowerBattery@@QEAAHAEAHAEAW4ITSMode@12@@Z", SetLastError = true)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int GetITSMode(ref CIntelligentCooling instance, ref int itsVersion, ref ITSMode itsMode);

    [LibraryImport("PowerBattery.dll", EntryPoint = "?GetDispatcherVersion@CIntelligentCooling@PowerBattery@@QEAAHXZ", SetLastError = true)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int GetDispatcherVersion(ref CIntelligentCooling instance);

    [LibraryImport("PowerBattery.dll", EntryPoint = "?GetDispatcherMode@CIntelligentCooling@PowerBattery@@QEAAHAEAHAEAW4ITSMode@12@H@Z", SetLastError = true)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int GetDispatcherMode(ref CIntelligentCooling instance, ref int supportItsMode, ref ITSMode itsMode, int geekModeFlag);

    [LibraryImport("PowerBattery.dll", EntryPoint = "?GetITSVersion@CIntelligentCooling@PowerBattery@@QEAAHXZ", SetLastError = true)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int GetITSVersion(ref CIntelligentCooling instance);

    [LibraryImport("PowerBattery.dll", EntryPoint = "?SetDispatcherMode@CIntelligentCooling@PowerBattery@@QEAAHAEAW4ITSMode@12@H@Z", SetLastError = true)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int SetDispatcherMode(ref CIntelligentCooling instance, ref ITSMode itsMode, int var);

    [LibraryImport("PowerBattery.dll", EntryPoint = "?HasDispatcherDeviceNode@CIntelligentCooling@PowerBattery@@QEAAHXZ", SetLastError = true)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int HasDispatcherDeviceNode(ref CIntelligentCooling instance);
    #endregion

    public ITSMode LastItsMode { get; set; } = ITSMode.None;

    public async Task<bool> IsSupportedAsync()
    {
        var machineInfo = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        return machineInfo.Properties.SupportsITSMode;
    }

    public async Task<ITSMode[]> GetAllStatesAsync()
    {
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

        if (mi.LegionSeries == LegionSeries.ThinkBook)
        {
            return Enum.GetValues(typeof(ITSMode))
                       .Cast<ITSMode>()
                       .Where(mode => mode != ITSMode.None)
                       .ToArray();
        }
        else
        {
            return Enum.GetValues(typeof(ITSMode))
                       .Cast<ITSMode>()
                       .Where(mode => mode != ITSMode.MmcGeek && mode != ITSMode.None)
                       .ToArray();
        }
    }

    public async Task<ITSMode> GetStateAsync()
    {
        try
        {
            return await Task.Run(GetItsModeInternal).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to get ITS mode", ex);

            return ITSMode.None;
        }
    }

    public async Task SetStateAsync(ITSMode state)
    {
        Log.Instance.Trace($"Setting ITS mode to: {state}");

        try
        {
            await Task.Run(() => SetItsModeInternal(state)).ConfigureAwait(false);
            LastItsMode = state;

            Log.Instance.Trace($"ITS mode set successfully to: {state}");

            PublishNotification(state);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to set ITS mode to {state}", ex);

            throw;
        }
    }

    public async Task ToggleItsMode()
    {
        try
        {
            var currentState = await GetStateAsync().ConfigureAwait(false);
            var allStates = await GetAllStatesAsync().ConfigureAwait(false);
            var availableStates = allStates.Where(state => state != ITSMode.None).ToArray();

            if (availableStates.Length == 0)
            {
                return;
            }

            ITSMode nextState;

            if (currentState == ITSMode.None)
            {
                nextState = LastItsMode != ITSMode.None && availableStates.Contains(LastItsMode)
                    ? LastItsMode
                    : availableStates[0];
            }
            else
            {
                var currentIndex = Array.IndexOf(availableStates, currentState);
                nextState = availableStates[(currentIndex + 1) % availableStates.Length];
            }

            Log.Instance.Trace($"Toggling ITS mode: {currentState} -> {nextState}");

            await SetStateAsync(nextState).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to toggle ITS mode", ex);
        }
    }

    private ITSMode GetItsModeInternal()
    {
        try
        {
            CIntelligentCooling instance = default;
            var machineInfo = Compatibility.GetMachineInformationAsync().Result;
            var isThinkBook = machineInfo.LegionSeries == LegionSeries.ThinkBook;

            return HasDispatcherDeviceNode(ref instance) != 0 ? GetDispatcherModeInternal(ref instance, isThinkBook) : GetStandardModeInternal(ref instance);
        }
        catch (DllNotFoundException)
        {
            return ITSMode.None;
        }
    }

    private ITSMode GetDispatcherModeInternal(ref CIntelligentCooling instance, bool isThinkBook)
    {
        var supportFlag = 0;
        var mode = ITSMode.None;
        var errorCode = GetDispatcherMode(ref instance, ref supportFlag, ref mode, isThinkBook ? 1 : 0);

        Log.Instance.Trace($"GetDispatcherMode() executed. Error Code: {errorCode}");
        LogSupportedModes(supportFlag);

        return mode;
    }

    private ITSMode GetStandardModeInternal(ref CIntelligentCooling instance)
    {
        var version = 0;
        var mode = ITSMode.None;
        var errorCode = GetITSMode(ref instance, ref version, ref mode);

        Log.Instance.Trace($"GetITSMode() executed. Error Code: {errorCode}");
        Log.Instance.Trace($"ITS Version: {version}");
        return mode;
    }

    private void SetItsModeInternal(ITSMode state)
    {
        CIntelligentCooling instance = default;
        var machineInfo = Compatibility.GetMachineInformationAsync().Result;
        var isThinkBook = machineInfo.LegionSeries == LegionSeries.ThinkBook;
        var dispatcherVersion = GetDispatcherVersion(ref instance);

        int errorCode;
        if (dispatcherVersion >= DISPATCHER_VERSION_3)
        {
            errorCode = SetDispatcherMode(ref instance, ref state, isThinkBook ? 1 : 0);
            Log.Instance.Trace($"Using SetDispatcherMode()");
            Log.Instance.Trace($"SetDispatcherMode executed. Error Code: {errorCode}");
        }
        else
        {
            errorCode = SetITSMode(ref instance, ref state);
            Log.Instance.Trace($"Using SetITSMode()");
            Log.Instance.Trace($"SetITSMode executed. Error Code: {errorCode}");
        }

        VerifyModeChange(ref instance, state);
    }

    private void VerifyModeChange(ref CIntelligentCooling instance, ITSMode expectedMode)
    {
        var version = 0;
        var currentMode = ITSMode.None;
        GetITSMode(ref instance, ref version, ref currentMode);

        Log.Instance.Trace($"Mode verification - Expected: {expectedMode}, Actual: {currentMode}, Match: {expectedMode == currentMode}");
    }

    private void LogSupportedModes(int supportFlag)
    {
        if (!Log.Instance.IsTraceEnabled)
        {
            return;
        }

        var modes = new[]
        {
            (1, ITSMode.ItsAuto),
            (2, ITSMode.MmcCool),
            (8, ITSMode.MmcPerformance),
            (16, ITSMode.MmcGeek)
        };

        foreach (var (flag, mode) in modes)
        {
            if ((supportFlag & flag) != 0)
            {
                Log.Instance.Trace($"Support ITSMode: {mode}");
            }
        }
    }

    private static void PublishNotification(ITSMode value)
    {
        switch (value)
        {
            case ITSMode.ItsAuto:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.ITSModeAuto, value.GetDisplayName()));
                break;
            case ITSMode.MmcCool:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.ITSModeCool, value.GetDisplayName()));
                break;
            case ITSMode.MmcPerformance:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.ITSModePerformance, value.GetDisplayName()));
                break;
            case ITSMode.MmcGeek:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.ITSModeGeek, value.GetDisplayName()));
                break;
        }
    }
}