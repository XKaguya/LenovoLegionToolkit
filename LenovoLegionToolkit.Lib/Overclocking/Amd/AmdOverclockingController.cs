using LenovoLegionToolkit.Lib.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Resources;
using ZenStates.Core;

namespace LenovoLegionToolkit.Lib.Overclocking.Amd;

public class OverclockingProfile
{
    public uint? FMax { get; set; }
    public bool ProchotEnabled { get; set; }
    public List<double?> CoreValues { get; set; } = new();
}

public class AmdOverclockingController : IDisposable
{
    private Cpu? _cpu;
    private MachineInformation? _machineInformation;
    private bool _isInitialized;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _internalProfilePath = Path.Combine(Folders.AppData, "amd_overclocking.json");
    private readonly string _statusFilePath = Path.Combine(Folders.AppData, "system_status.json");

    private const uint PROCHOT_DISABLED_BIT = 0x1000000;
    private const int THERSHOLD = 3;

    public bool DoNotApply = false;

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isInitialized) return;
            _machineInformation = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
            _cpu = new Cpu();
            _isInitialized = true;

            var info = LoadShutdownInfo();

            int currentCount = info.AbnormalCount;
            string currentStatus = info.Status;

            if (currentStatus == "Running")
            {
                currentCount++;
                Log.Instance.Trace($"Abnormal shutdown detected, current count: {currentCount}");
            }

            if (currentCount >= THERSHOLD)
            {
                DoNotApply = true;
                Log.Instance.Trace($"Abnormal shutdown reached limit: ({THERSHOLD}), Will not apply profile.");

                currentCount = 0;
            }

            var nextInfo = new ShutdownInfo
            {
                Status = "Running",
                AbnormalCount = currentCount
            };

            SaveShutdownInfo(nextInfo);
        }
        finally { _lock.Release(); }
    }

    public bool IsSupported() => _isInitialized && (_machineInformation?.Properties.IsAmdDevice == true);

    public bool IsActive() => File.Exists(Path.Combine(Folders.AppData, "amd_overclocking.json"));

    public Cpu GetCpu() => _cpu ?? throw new InvalidOperationException($"{Resource.AmdOverclocking_Not_Initialized_Message}");

    public ShutdownInfo LoadShutdownInfo()
    {
        if (!File.Exists(_statusFilePath)) return new ShutdownInfo { Status = "Normal", AbnormalCount = 0 };
        try
        {
            return JsonSerializer.Deserialize<ShutdownInfo>(File.ReadAllText(_statusFilePath));
        }
        catch
        {
            return new ShutdownInfo { Status = "Normal", AbnormalCount = 0 };
        }
    }

    public void SaveShutdownInfo(ShutdownInfo info)
    {
        try
        {
            File.WriteAllText(_statusFilePath, JsonSerializer.Serialize(info));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Save ShutdownInfo failed: {ex.Message}");
        }
    }

    public OverclockingProfile? LoadProfile(string? path = null)
    {
        string targetPath = path ?? _internalProfilePath;
        if (!File.Exists(targetPath)) return null;

        try
        {
            var json = File.ReadAllText(targetPath);
            return JsonSerializer.Deserialize<OverclockingProfile>(json);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Load Failed: {ex.Message}");
            return null;
        }
    }

    public void SaveProfile(OverclockingProfile profile, string? path = null)
    {
        try
        {
            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path ?? _internalProfilePath, json);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Save Failed: {ex.Message}");
        }
    }

    public async Task ApplyProfileAsync(OverclockingProfile profile)
    {
        if (DoNotApply)
        {
            return;
        }

        EnsureInitialized();
        await Task.Run(() =>
        {
            EnableOCMode(profile.ProchotEnabled);

            if (profile.FMax.HasValue)
            {
                _cpu.SetFMax(profile.FMax.Value);
                Log.Instance.Trace($"FMax set to {profile.FMax.Value}");
            }

            if (_cpu.smu.Rsmu.SMU_MSG_SetDldoPsmMargin != 0)
            {
                for (int i = 0; i < profile.CoreValues.Count && i < 16; i++)
                {
                    var val = profile.CoreValues[i];
                    if (val.HasValue)
                    {
                        if (!IsCoreActive(i))
                        {
                            continue;
                        }

                        _cpu.SetPsmMarginSingleCore(EncodeCoreMarginBitmask(i), (int)val.Value);
                        Log.Instance.Trace($"Core {i} set to {(int)val.Value}");
                    }
                }
            }
        }).ConfigureAwait(true);
    }

    public async Task ApplyInternalProfileAsync()
    {
        var profile = LoadProfile();
        if (profile != null)
        {
            await ApplyProfileAsync(profile).ConfigureAwait(false);
        }
    }

    public bool EnableOCMode(bool prochotEnabled = true)
    {
        EnsureInitialized();
        var args = MakeCmdArgs(prochotEnabled ? 0U : PROCHOT_DISABLED_BIT, _cpu.smu.Rsmu.MAX_ARGS);
        return _cpu.smu.SendSmuCommand(_cpu.smu.Rsmu, _cpu.smu.Rsmu.SMU_MSG_EnableOcMode, ref args) == SMU.Status.OK;
    }

    public uint EncodeCoreMarginBitmask(int coreIndex, int coresPerCCD = 8)
    {
        EnsureInitialized();
        if (_cpu.smu.SMU_TYPE is >= SMU.SmuType.TYPE_APU0 and <= SMU.SmuType.TYPE_APU2)
            return (uint)coreIndex;

        int ccdIndex = coreIndex / coresPerCCD;
        int localCoreIndex = coreIndex % coresPerCCD;
        return (uint)(((ccdIndex << 8) | localCoreIndex) << 20);
    }

    public bool IsCoreActive(int coreIndex)
    {
        EnsureInitialized();
        int mapIndex = coreIndex < 8 ? 0 : 1;
        return ((~_cpu.info.topology.coreDisableMap[mapIndex] >> (coreIndex % 8)) & 1) == 1;
    }

    public static uint[] MakeCmdArgs(uint arg = 0, uint maxArgs = 6)
    {
        var cmdArgs = new uint[maxArgs];
        cmdArgs[0] = arg;
        return cmdArgs;
    }

    [MemberNotNull(nameof(_cpu), nameof(_machineInformation))]
    private void EnsureInitialized()
    {
        if (!_isInitialized || _cpu == null || _machineInformation == null)
        {
            throw new InvalidOperationException($"{Resource.AmdOverclocking_Not_Initialized_Message}");
        }
    }

    public void Dispose()
    {
        if (_cpu is IDisposable d)
        {
            d.Dispose();
        }

        _lock.Dispose();
        GC.SuppressFinalize(this);
    }
}