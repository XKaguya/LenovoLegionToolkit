using LenovoLegionToolkit.Lib.Utils;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Features;

public partial class ITSModeFeature : IFeature<ITSMode>
{
    #region Import
    [LibraryImport("PowerBattery.dll", EntryPoint = "?SetITSMode@CIntelligentCooling@PowerBattery@@QEAAHAEAW4ITSMode@12@@Z", SetLastError = true)]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    internal static partial int SetITSMode(ref CIntelligentCooling instance, ref ITSMode itsMode);

    [LibraryImport("PowerBattery.dll", EntryPoint = "?GetITSMode@CIntelligentCooling@PowerBattery@@QEAAHAEAHAEAW4ITSMode@12@@Z", SetLastError = true)]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    internal static partial int GetITSMode(ref CIntelligentCooling instance, ref int var2, ref ITSMode itsMode);

    [LibraryImport("PowerBattery.dll", EntryPoint = "?GetDispatcherVersion@CIntelligentCooling@PowerBattery@@QEAAHXZ", SetLastError = true)]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    internal static partial int GetDispatcherVersion(ref CIntelligentCooling instance);

    [LibraryImport("PowerBattery.dll", EntryPoint = "?GetITSVersion@CIntelligentCooling@PowerBattery@@QEAAHXZ", SetLastError = true)]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]   
    internal static partial int GetITSVersion(ref CIntelligentCooling instance);

    [LibraryImport("PowerBattery.dll", EntryPoint = "?SetDispatcherMode@CIntelligentCooling@PowerBattery@@QEAAHAEAW4ITSMode@12@H@Z", SetLastError = true)]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    internal static partial int SetDispatcherMode(ref CIntelligentCooling instance, ref ITSMode itsMode, int var);

    private static uint ITS_VERSION_3 = 16384U;
    private static uint ITS_VERSION_4 = 20480U;
    private static uint ITS_VERSION_5 = 24576U;
    private static uint DISPATCHER_VERSION_2 = 4096U;
    private static uint DISPATCHER_VERSION_3 = 8192U;
    private static uint DISPATCHER_VERSION_4 = 12288U;
    #endregion

    public ITSMode LastItsMode { get; set; }

    public async Task<bool> IsSupportedAsync()
    {
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        return mi.Properties.SupportITSMode;
    }

    public Task<ITSMode[]> GetAllStatesAsync()
    {
        return Task.FromResult((ITSMode[])Enum.GetValues(typeof(ITSMode)));
    }

    public Task<ITSMode> GetStateAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                int num = 0;
                ITSMode itsmode = ITSMode.None;
                CIntelligentCooling instance = default;

                try
                {
                    int errorCode = GetITSMode(ref instance, ref num, ref itsmode);

                    if (Log.Instance.IsTraceEnabled)
                    {
                        Log.Instance.Trace($"GetITSMode() executed. Error Code: {errorCode}");
                    }
                }
                catch (DllNotFoundException)
                {
                    return ITSMode.None;
                }

                LastItsMode = itsmode;
                return itsmode;
            }
            catch (Exception ex)
            {
                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"Failed to get ITS mode", ex);
                }

                return ITSMode.None;
            }
        });
    }

    public Task SetStateAsync(ITSMode itsMode)
    {
        return Task.Run(() =>
        {
            if (Log.Instance.IsTraceEnabled)
            {
                Log.Instance.Trace($"Starting ITS mode setting: {itsMode}");
            }

            try
            {
                CIntelligentCooling instance = default;
                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"CIntelligentCooling instance initialized with default value: {instance}");
                }

                var mi = Compatibility.GetMachineInformationAsync().Result;
                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"Machine information retrieved - Series: {mi.LegionSeries}, IsThinkBook: {mi.LegionSeries == LegionSeries.ThinkBook}");
                }

                var flag = mi.LegionSeries == LegionSeries.ThinkBook;
                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"Is ThinkBook and support Geek Mode: {flag}");
                }

                var version = GetDispatcherVersion(ref instance);
                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"Dispatcher version: {version} (threshold: {DISPATCHER_VERSION_3})");
                }

                if (version >= DISPATCHER_VERSION_3)
                {
                    if (Log.Instance.IsTraceEnabled)
                    {
                        Log.Instance.Trace($"Using SetDispatcherMode()");
                    }

                    int? num = SetDispatcherMode(ref instance, ref itsMode, flag ? 1 : 0);

                    if (Log.Instance.IsTraceEnabled)
                    {
                        Log.Instance.Trace($"SetDispatcherMode executed. Error Code: {num}");
                    }
                }
                else
                {
                    if (Log.Instance.IsTraceEnabled)
                    {
                        Log.Instance.Trace($"Using SetITSMode()");
                    }

                    int? num = SetITSMode(ref instance, ref itsMode);

                    if (Log.Instance.IsTraceEnabled)
                    {
                        Log.Instance.Trace($"SetITSMode executed. Error Code: {num}");
                    }
                }

                LastItsMode = itsMode;
                ITSMode currentMode = ITSMode.None;
                int garbage = 0;
                GetITSMode(ref instance, ref garbage, ref currentMode);
                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"ITS mode set successfully, LastItsMode updated to: {itsMode}");
                    Log.Instance.Trace($"LastItsMode == currentMode {LastItsMode == currentMode}");
                }
            }
            catch (DllNotFoundException)
            {
                throw new DllNotFoundException("PowerBattery.dll not found.");
            }
            catch (Exception ex)
            {
                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"Failed to set ITS mode to {itsMode}", ex);
                }
            }
            finally
            {
                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"ITS mode setting operation completed");
                }
            }
        });
    }
}
