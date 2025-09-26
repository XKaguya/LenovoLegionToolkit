using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Features;

public partial class ItsModeFeature : IFeature<ITSMode>
{
    public ITSMode LastItsMode { get; set; }

    [LibraryImport("PowerBattery.dll", EntryPoint = "?SetITSMode@CIntelligentCooling@PowerBattery@@QEAAHAEAW4ITSMode@12@@Z", SetLastError = true)]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    internal static partial int SetITSMode(ref CIntelligentCooling var1, ref ITSMode var2);

    [LibraryImport("PowerBattery.dll", EntryPoint = "?GetITSMode@CIntelligentCooling@PowerBattery@@QEAAHAEAHAEAW4ITSMode@12@@Z", SetLastError = true)]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    internal static partial int GetITSMode(ref CIntelligentCooling var1, ref int var2, ref ITSMode var3);

    public Task<bool> IsSupportedAsync()
    {
        return Task.FromResult(true);
    }

    public Task<ITSMode[]> GetAllStatesAsync()
    {
        return Task.FromResult((ITSMode[])Enum.GetValues(typeof(ITSMode)));
    }

    public Task<ITSMode> GetStateAsync()
    {
        return Task.Run(() =>
        {
            int num = 0;
            ITSMode itsmode = ITSMode.None;
            CIntelligentCooling instance = default;

            try
            {
                GetITSMode(ref instance, ref num, ref itsmode);
            }
            catch (DllNotFoundException)
            {
                return ITSMode.None;
            }

            LastItsMode = itsmode;
            return itsmode;
        });
    }

    public Task SetStateAsync(ITSMode itsMode)
    {
        return Task.Run(() =>
        {
            CIntelligentCooling instance = default;
            SetITSMode(ref instance, ref itsMode);
            LastItsMode = itsMode;
        });
    }
}