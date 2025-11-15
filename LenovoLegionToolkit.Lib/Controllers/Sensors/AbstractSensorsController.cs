using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Diagnostics;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using Windows.Win32;
using Windows.Win32.System.Power;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors;

public abstract class AbstractSensorsController(GPUController gpuController) : ISensorsController
{
    protected readonly struct GPUInfo(
        int utilization,
        int coreClock,
        int maxCoreClock,
        int memoryClock,
        int maxMemoryClock,
        int temperature,
        int maxTemperature)
    {
        public static readonly GPUInfo Empty = new(-1, -1, -1, -1, -1, -1, -1);

        public int Utilization { get; } = utilization;
        public int CoreClock { get; } = coreClock;
        public int MaxCoreClock { get; } = maxCoreClock;
        public int MemoryClock { get; } = memoryClock;
        public int MaxMemoryClock { get; } = maxMemoryClock;
        public int Temperature { get; } = temperature;
        public int MaxTemperature { get; } = maxTemperature;
    }

    private readonly SafePerformanceCounter _percentProcessorPerformanceCounter = new("Processor Information", "% Processor Performance", "_Total");
    private readonly SafePerformanceCounter _percentProcessorUtilityCounter = new("Processor", "% Processor Time", "_Total");
    private readonly SafePerformanceCounter _percentProcessorIdleCounter = new("Processor", "% Idle Time", "_Total");
    private PerformanceCounter[]? _perCoreCounters;

    protected int? _cpuBaseClockCache;
    protected int? _cpuMaxCoreClockCache;
    protected int? _cpuMaxFanSpeedCache;
    protected int? _gpuMaxFanSpeedCache;
    protected int? _pchMaxFanSpeedCache;

    public abstract Task<bool> IsSupportedAsync();

    public async Task PrepareAsync()
    {
        _percentProcessorPerformanceCounter.Reset();
        _percentProcessorUtilityCounter.Reset();
        _percentProcessorIdleCounter.Reset();

        try
        {
            var category = new PerformanceCounterCategory("Processor");
            var instances = category.GetInstanceNames().Where(n => n != "_Total").ToArray();
            if (instances.Length >0)
            {
                _perCoreCounters = instances.Select(inst => new PerformanceCounter("Processor", "% Processor Time", inst, readOnly: true)).ToArray();
                foreach (var pc in _perCoreCounters)
                    pc.NextValue();
            }
        }
        catch
        {
            _perCoreCounters = null;
        }

        _percentProcessorUtilityCounter.NextValue();
        _percentProcessorPerformanceCounter.NextValue();
        _percentProcessorIdleCounter.NextValue();

        if (_perCoreCounters != null)
        {
            foreach (var pc in _perCoreCounters)
                pc.NextValue();
        }

        await Task.Delay(500).ConfigureAwait(false);

        _percentProcessorUtilityCounter.NextValue();
        _percentProcessorPerformanceCounter.NextValue();
        _percentProcessorIdleCounter.NextValue();

        if (_perCoreCounters != null)
        {
            foreach (var pc in _perCoreCounters)
                pc.NextValue();
        }
    }

    public virtual async Task<SensorsData> GetDataAsync()
    {
        const int genericMaxUtilization = 100;
        const int genericMaxTemperature = 100;

        var cpuUtilization = GetCpuUtilization(genericMaxUtilization);
        var cpuMaxCoreClock = _cpuMaxCoreClockCache ??= await GetCpuMaxCoreClockAsync().ConfigureAwait(false);
        var cpuCoreClock = GetCpuCoreClock();
        var cpuCurrentTemperature = await GetCpuCurrentTemperatureAsync().ConfigureAwait(false);
        var cpuCurrentFanSpeed = await GetCpuCurrentFanSpeedAsync().ConfigureAwait(false);
        var cpuMaxFanSpeed = _cpuMaxFanSpeedCache ??= await GetCpuMaxFanSpeedAsync().ConfigureAwait(false);

        var gpuInfo = await GetGPUInfoAsync().ConfigureAwait(false);
        var gpuCurrentTemperature = gpuInfo.Temperature >= 0 ? gpuInfo.Temperature : await GetGpuCurrentTemperatureAsync().ConfigureAwait(false);
        var gpuMaxTemperature = gpuInfo.MaxTemperature >= 0 ? gpuInfo.MaxTemperature : genericMaxTemperature;
        var gpuCurrentFanSpeed = await GetGpuCurrentFanSpeedAsync().ConfigureAwait(false);
        var gpuMaxFanSpeed = _gpuMaxFanSpeedCache ??= await GetGpuMaxFanSpeedAsync().ConfigureAwait(false);

        var pchCurrentTemperature = await GetPchCurrentTemperatureAsync().ConfigureAwait(false);
        var pchMaxTemperature = genericMaxTemperature;
        var pchCurrentFanSpeed = await GetPchCurrentFanSpeedAsync().ConfigureAwait(false);
        var pchMaxFanSpeed = _pchMaxFanSpeedCache ??= await GetPchMaxFanSpeedAsync().ConfigureAwait(false);

        var cpu = new SensorData(cpuUtilization,
            genericMaxUtilization,
            cpuCoreClock,
            cpuMaxCoreClock,
            -1,
            -1,
            cpuCurrentTemperature,
            genericMaxTemperature,
            cpuCurrentFanSpeed,
            cpuMaxFanSpeed);
        var gpu = new SensorData(gpuInfo.Utilization,
            genericMaxUtilization,
            gpuInfo.CoreClock,
            gpuInfo.MaxCoreClock,
            gpuInfo.MemoryClock,
            gpuInfo.MaxMemoryClock,
            gpuCurrentTemperature,
            gpuMaxTemperature,
            gpuCurrentFanSpeed,
            gpuMaxFanSpeed);
        var pch = new SensorData(
            -1,
            -1,
            -1,
            -1,
            -1,
            -1,
            pchCurrentTemperature,
            pchMaxTemperature,
            pchCurrentFanSpeed,
            pchMaxFanSpeed);
        var result = new SensorsData(cpu, gpu, pch);

        return result;
    }

    public async Task<FanSpeedTable> GetFanSpeedsAsync()
    {
        var cpuFanSpeed = await GetCpuCurrentFanSpeedAsync().ConfigureAwait(false);
        var gpuFanSpeed = await GetGpuCurrentFanSpeedAsync().ConfigureAwait(false);
        var pchFanSpeed = await GetPchCurrentFanSpeedAsync().ConfigureAwait(false);
        return new FanSpeedTable(cpuFanSpeed, gpuFanSpeed, pchFanSpeed);
    }

    protected abstract Task<int> GetCpuCurrentTemperatureAsync();

    protected abstract Task<int> GetGpuCurrentTemperatureAsync();

    protected virtual Task<int> GetPchCurrentTemperatureAsync() => Task.FromResult(-1);

    protected abstract Task<int> GetCpuCurrentFanSpeedAsync();

    protected abstract Task<int> GetGpuCurrentFanSpeedAsync();

    protected virtual Task<int> GetPchCurrentFanSpeedAsync() => Task.FromResult(-1);

    protected abstract Task<int> GetCpuMaxFanSpeedAsync();

    protected abstract Task<int> GetGpuMaxFanSpeedAsync();

    protected virtual Task<int> GetPchMaxFanSpeedAsync() => Task.FromResult(-1);

    protected static unsafe int GetCpuBaseClock()
    {
        var ptr = IntPtr.Zero;
        try
        {
            PInvoke.GetSystemInfo(out var systemInfo);

            var numberOfProcessors = Math.Min(32, (int)systemInfo.dwNumberOfProcessors);
            var infoSize = Marshal.SizeOf<PROCESSOR_POWER_INFORMATION>();
            var infosSize = numberOfProcessors * infoSize;

            ptr = Marshal.AllocHGlobal(infosSize);

            var result = PInvoke.CallNtPowerInformation(POWER_INFORMATION_LEVEL.ProcessorInformation,
                null,
                0,
                ptr.ToPointer(),
                (uint)infosSize);
            if (result != 0)
                return 0;

            var infos = new PROCESSOR_POWER_INFORMATION[numberOfProcessors];

            for (var i = 0; i < infos.Length; i++)
                infos[i] = Marshal.PtrToStructure<PROCESSOR_POWER_INFORMATION>(IntPtr.Add(ptr, i * infoSize));

            return (int)infos.Select(p => p.MaxMhz).Max();
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    protected static Task<int> GetCpuMaxCoreClockAsync() => WMI.LenovoGameZoneData.GetCPUFrequencyAsync();

    protected async Task<GPUInfo> GetGPUInfoAsync()
    {
        if (gpuController.IsSupported())
            await gpuController.StartAsync().ConfigureAwait(false);

        if (await gpuController.GetLastKnownStateAsync().ConfigureAwait(false) is GPUState.PoweredOff or GPUState.Unknown)
            return GPUInfo.Empty;

        try
        {
            NVAPI.Initialize();

            var gpu = NVAPI.GetGPU();
            if (gpu is null)
                return GPUInfo.Empty;

            var utilization = Math.Min(100, Math.Max(gpu.UsageInformation.GPU.Percentage, gpu.UsageInformation.VideoEngine.Percentage));

            var currentCoreClock = (int)gpu.CurrentClockFrequencies.GraphicsClock.Frequency / 1000;
            var currentMemoryClock = (int)gpu.CurrentClockFrequencies.MemoryClock.Frequency / 1000;

            var maxCoreClock = (int)gpu.BoostClockFrequencies.GraphicsClock.Frequency / 1000;
            var maxMemoryClock = (int)gpu.BoostClockFrequencies.MemoryClock.Frequency / 1000;

            switch (gpu.MemoryInformation.RAMType)
            {
                case GPUMemoryType.GDDR5:
                case GPUMemoryType.GDDR5X:
                    currentMemoryClock /= 2;
                    maxMemoryClock /= 2;
                    break;
                case GPUMemoryType.GDDR6:
                case GPUMemoryType.GDDR6X:
                    currentMemoryClock /= 4;
                    maxMemoryClock /= 4;
                    break;
                case GPUMemoryType.GDDR7:
                    currentMemoryClock /= 8;
                    maxMemoryClock /= 8;
                    break;
                default:
                    break;
            }

            var states = GPUApi.GetPerformanceStates20(gpu.Handle);
            var maxCoreClockOffset = states.Clocks[PerformanceStateId.P0_3DPerformance][0].FrequencyDeltaInkHz.DeltaValue / 1000;
            var maxMemoryClockOffset = states.Clocks[PerformanceStateId.P0_3DPerformance][1].FrequencyDeltaInkHz.DeltaValue / 1000;

            var temperatureSensor = gpu.ThermalInformation.ThermalSensors.FirstOrDefault();
            var currentTemperature = temperatureSensor?.CurrentTemperature ?? -1;
            var maxTemperature = temperatureSensor?.DefaultMaximumTemperature ?? -1;

            return new(utilization,
                currentCoreClock,
                maxCoreClock + maxCoreClockOffset,
                currentMemoryClock,
                maxMemoryClock + maxMemoryClockOffset,
                currentTemperature,
                maxTemperature);
        }
        catch
        {
            return GPUInfo.Empty;
        }
    }

    protected int GetCpuUtilization(int maxUtilization)
    {
        if (_perCoreCounters != null && _perCoreCounters.Length >0)
        {
            try
            {
                var values = _perCoreCounters.Select(pc => pc.NextValue()).ToArray();
                if (values.Length >0)
                {
                    var avg = values.Average();
                    if (!double.IsNaN(avg))
                    {
                        var rounded = (int)Math.Round(avg);
                        return Math.Min(Math.Max(rounded,0), maxUtilization);
                    }
                }
            }
            catch { }
        }

        var idleRaw = _percentProcessorIdleCounter.NextValue();
        if (!float.IsNaN(idleRaw) && idleRaw >=0)
        {
            var usage =100.0f - idleRaw;
            if (!float.IsNaN(usage) && usage >=0)
            {
                var rounded = (int)Math.Round(usage);
                return Math.Min(Math.Max(rounded,0), maxUtilization);
            }
        }

        var raw = _percentProcessorUtilityCounter.NextValue();
        if (float.IsNaN(raw) || raw <0)
            return -1;

        var roundedFallback = (int)Math.Round(raw);
        return Math.Min(Math.Max(roundedFallback,0), maxUtilization);
    }

    protected int GetCpuCoreClock()
    {
        var baseClock = _cpuBaseClockCache ??= GetCpuBaseClock();
        var perfValue = _percentProcessorPerformanceCounter.NextValue();
        if (float.IsNaN(perfValue) || perfValue <=0)
            return -1;

        var clock = (int)(baseClock * (perfValue /100f));
        if (clock <1)
            return -1;
        return clock;
    }
}
