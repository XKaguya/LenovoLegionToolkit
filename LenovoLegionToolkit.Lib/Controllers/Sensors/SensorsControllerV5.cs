using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors;

public class SensorsControllerV5(GPUController gpuController) : AbstractSensorsController(gpuController)
{
    private const int CPU_SENSOR_ID = 1;
    private const int GPU_SENSOR_ID = 5;
    private const int PCH_SENSOR_ID = 4;
    private const int CPU_FAN_ID = 1;
    private const int GPU_FAN_ID = 2;
    private const int PCH_FAN_ID = 4;

    public async Task<bool> IsSupportPchFanAsync()
    {
        try
        {
            var result = await WMI.LenovoFanTableData.ExistsAsync(PCH_SENSOR_ID, PCH_FAN_ID).ConfigureAwait(false);
            if (result)
                _ = await GetDataAsync().ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Error checking PCH fan support. [type={GetType().Name}]", ex);
            return false;
        }
    }

    public override async Task<bool> IsSupportedAsync()
    {
        try
        {
            var result = await WMI.LenovoFanTableData.ExistsAsync(CPU_SENSOR_ID, CPU_FAN_ID).ConfigureAwait(false);
            result &= await WMI.LenovoFanTableData.ExistsAsync(GPU_SENSOR_ID, GPU_FAN_ID).ConfigureAwait(false);
            result &= await WMI.LenovoFanTableData.ExistsAsync(PCH_SENSOR_ID, PCH_FAN_ID).ConfigureAwait(false);

            if (result)
                _ = await GetDataAsync().ConfigureAwait(false);

            return result;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Error checking support. [type={GetType().Name}]", ex);

            return false;
        }
    }

    public override async Task<SensorsData> GetDataAsync()
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
            120,
            pchCurrentFanSpeed,
            pchMaxFanSpeed);
        var result = new SensorsData(cpu, gpu, pch);

        return result;
    }

    public async Task<(int cpuFanSpeed, int gpuFanSpeed, int pchFanSpeed)> GetAllFanSpeedsAsync()
    {
        var cpuFanSpeed = await GetCpuCurrentFanSpeedAsync().ConfigureAwait(false);
        var gpuFanSpeed = await GetGpuCurrentFanSpeedAsync().ConfigureAwait(false);
        var pchFanSpeed = await GetPchCurrentFanSpeedAsync().ConfigureAwait(false);
        return (cpuFanSpeed, gpuFanSpeed, pchFanSpeed);
    }

    protected override async Task<int> GetCpuCurrentTemperatureAsync()
    {
        var value = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.CpuCurrentTemperature).ConfigureAwait(false);
        return value < 1 ? -1 : value;
    }

    protected override async Task<int> GetGpuCurrentTemperatureAsync()
    {
        var value = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.GpuCurrentTemperature).ConfigureAwait(false);
        return value < 1 ? -1 : value;
    }
    protected async Task<int> GetPchCurrentTemperatureAsync()
    {
        var value = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.PchCurrentTemperature).ConfigureAwait(false);
        return value < 1 ? -1 : value;
    }

    protected override Task<int> GetCpuCurrentFanSpeedAsync() => WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.CpuCurrentFanSpeed);

    protected override Task<int> GetGpuCurrentFanSpeedAsync() => WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.GpuCurrentFanSpeed);
    protected Task<int> GetPchCurrentFanSpeedAsync() => WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.PchCurrentFanSpeed);

    protected override Task<int> GetCpuMaxFanSpeedAsync() => WMI.LenovoFanMethod.GetCurrentFanMaxSpeedAsync(CPU_SENSOR_ID, CPU_FAN_ID);

    protected override Task<int> GetGpuMaxFanSpeedAsync() => WMI.LenovoFanMethod.GetCurrentFanMaxSpeedAsync(GPU_SENSOR_ID, GPU_FAN_ID);
    protected Task<int> GetPchMaxFanSpeedAsync() => WMI.LenovoFanMethod.GetCurrentFanMaxSpeedAsync(PCH_SENSOR_ID, PCH_FAN_ID);
}
