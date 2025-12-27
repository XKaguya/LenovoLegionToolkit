using LenovoLegionToolkit.Lib.Utils;
using System;
using System.Threading;
using System.Threading.Tasks;
using ZenStates.Core;

namespace LenovoLegionToolkit.Lib.Overclocking.Amd;

public class AmdOverclockingController : IDisposable
{
    private Cpu _cpu;
    private MachineInformation _machineInformation;
    private bool _isInitialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private const uint PROCHOT_DISABLED_BIT = 0x1000000;

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isInitialized) return;

            _machineInformation = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

            _cpu = new Cpu();
            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public bool IsSupported() => _isInitialized && (_machineInformation.Properties.IsAmdDevice);

    public Cpu GetCpu()
    {
        return _cpu;
    }

    public uint GetFMaxFrequency()
    {
        EnsureInitialized();
        return _cpu.GetFMax();
    }

    public bool EnableOCMode(bool prochotEnabled = true)
    {
        EnsureInitialized();

        var val = prochotEnabled ? 0U : PROCHOT_DISABLED_BIT;
        var args = MakeCmdArgs(val, _cpu.smu.Rsmu.MAX_ARGS);

        var status = _cpu.smu.SendSmuCommand(
            _cpu.smu.Rsmu,
            _cpu.smu.Rsmu.SMU_MSG_EnableOcMode,
            ref args
        );

        return status == SMU.Status.OK;
    }

    public bool DisableOCMode()
    {
        EnsureInitialized();
        return _cpu.DisableOcMode() == SMU.Status.OK;
    }

    public static uint[] MakeCmdArgs(uint arg = 0, uint maxArgs = 6) => MakeCmdArgs([arg], maxArgs);

    public static uint[] MakeCmdArgs(uint[] args, uint maxArgs = 6)
    {
        var cmdArgs = new uint[maxArgs];
        if (args == null)
        {
            return cmdArgs;
        }

        var length = Math.Min((int)maxArgs, args.Length);
        Array.Copy(args, cmdArgs, length);

        return cmdArgs;
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("Controller must be initialized before use.");
        }
    }

    public void Dispose()
    {
        if (!_isInitialized)
        {
            return;
        }

        if (_cpu is IDisposable disposableCpu)
        {
            disposableCpu.Dispose();
        }

        _initLock.Dispose();
        _isInitialized = false;

        GC.SuppressFinalize(this);
    }
}