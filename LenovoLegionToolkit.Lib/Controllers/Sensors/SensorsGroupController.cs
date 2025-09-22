﻿// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) RAMSPDToolkit and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors
{
    public class SensorsGroupController : IDisposable
    {
        private bool _initialized;
        private float _lastGpuPower;
        private readonly SemaphoreSlim _initSemaphore = new SemaphoreSlim(1, 1);
        private readonly List<IHardware> _interestedHardwares = new();

        private Computer? _computer;
        private IHardware? _cpuHardware;
        private IHardware? _amdGpuHardware;
        private IHardware? _gpuHardware;
        private IHardware? _memoryHardware;

        private bool _needRefreshGpuHardware;
        private string _cachedCpuName = string.Empty;
        private string _cachedGpuName = string.Empty;

        private readonly object _hardwareLock = new object();
        private volatile bool _hardwaresInitialized;

        private GPUController _gpuController = IoCContainer.Resolve<GPUController>();

        public async Task<bool> IsSupportedAsync()
        {
            try
            {
                var result = await InitializeAsync();
                if (result == 0)
                {
                    return false;
                }
                return _interestedHardwares.Any();
            }
            catch (Exception ex)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Memory sensor check failed: {ex}");
                return false;
            }
        }

        private void GetInterestedHardwares()
        {
            lock (_hardwareLock)
            {
                if (_hardwaresInitialized) return;

                try
                {
                    _computer = new Computer
                    {
                        IsCpuEnabled = true,
                        IsGpuEnabled = true,
                        IsMemoryEnabled = true,
                        IsMotherboardEnabled = false,
                        IsControllerEnabled = false,
                        IsNetworkEnabled = false,
                        IsStorageEnabled = true
                    };

                    _computer.Open();
                    _computer.Accept(new UpdateVisitor());

                    if (Log.Instance.IsTraceEnabled)
                    {
                        foreach (var hardware in _computer.Hardware)
                        {
                            Log.Instance.Trace($"Detected hardware: {hardware.HardwareType} - {hardware.Name}");
                        }
                    }

                    _interestedHardwares.AddRange(_computer.Hardware);
                    _cpuHardware = _interestedHardwares.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
                    _amdGpuHardware = _interestedHardwares.FirstOrDefault(h => h.HardwareType == HardwareType.GpuAmd);
                    _gpuHardware = _interestedHardwares.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia);
                    _memoryHardware = _interestedHardwares.FirstOrDefault(h => h.HardwareType == HardwareType.Memory);
                }
                finally
                {
                    _hardwaresInitialized = true;
                }
            }
        }

        public async Task<string> GetCpuNameAsync()
        {
            if (!await IsSupportedAsync().ConfigureAwait(false))
            {
                return "UNKNOWN";
            }

            if (_cpuHardware == null)
            {
                return "UNKNOWN";
            }

            if (!string.IsNullOrEmpty(_cachedCpuName))
            {
                return _cachedCpuName;
            }

            _cachedCpuName = StripName(_cpuHardware.Name);
            return _cachedCpuName;
        }

        public async Task<string> GetGpuNameAsync()
        {
            if (!await IsSupportedAsync().ConfigureAwait(false))
            {
                return "UNKNOWN";
            }

            if (string.IsNullOrEmpty(_cachedGpuName) || _needRefreshGpuHardware)
            {
                var gpu = _gpuHardware ?? _amdGpuHardware;
                _cachedGpuName = gpu != null ? StripName(gpu.Name) : "UNKNOWN";
                _needRefreshGpuHardware = false;
            }

            return _cachedGpuName;
        }


        public async Task<float> GetCpuPowerAsync()
        {
            if (!await IsSupportedAsync().ConfigureAwait(false))
            {
                return 0;
            }

            if (_cpuHardware == null)
            {
                return 0;
            }

            _cpuHardware.Update();

            var sensor = _cpuHardware.Sensors?
              .FirstOrDefault(s => s.SensorType == SensorType.Power);

            return sensor?.Value ?? 0;
        }

        public async Task<float> GetGpuPowerAsync()
        {
            if (!await IsSupportedAsync().ConfigureAwait(false))
            {
                return -1;
            }

            var state = await _gpuController.GetLastKnownStateAsync();
            if (_lastGpuPower <= 10 &&
                (state == GPUState.Inactive ||
                 state == GPUState.PoweredOff ||
                 state == GPUState.Unknown ||
                 state == GPUState.NvidiaGpuNotFound))
            {
                return -1;
            }

            if (_gpuHardware == null)
            {
                return -1;
            }

            try
            {
                _gpuHardware.Update();

                var sensor = _gpuHardware.Sensors?
                  .FirstOrDefault(s => s.SensorType == SensorType.Power);
                _lastGpuPower = sensor?.Value ?? 0;
                return sensor?.Value ?? 0;
            }
            catch (Exception ex)
            {
                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"GetGpuPowerAsync() raised exception: ", ex);
                }

                return -1;
            }
        }

        public async Task<float> GetGpuVramTemperatureAsync()
        {
            if (!await IsSupportedAsync().ConfigureAwait(false))
            {
                return 0;
            }

             if (_lastGpuPower <= 10 && (await _gpuController.GetLastKnownStateAsync() == GPUState.Inactive || await _gpuController.GetLastKnownStateAsync() == GPUState.PoweredOff))
            {
                return 0;
            }

            if (_gpuHardware == null)
            {
                return 0;
            }

            _gpuHardware.Update();
            var sensor = _gpuHardware.Sensors?
              .FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("GPU Memory Junction", StringComparison.OrdinalIgnoreCase));
            return sensor?.Value ?? 0;
        }

        public async Task<(float, float)> GetSSDTemperaturesAsync()
        {
            if (!await IsSupportedAsync().ConfigureAwait(false))
            {
                return (0, 0);
            }

            var temps = new List<float>();

            try
            {
                var storageHardwares = _interestedHardwares
                    .Where(h => h.HardwareType == HardwareType.Storage)
                    .ToList();

                if (storageHardwares.Count == 0)
                {
                    return (0, 0);
                }

                storageHardwares.ForEach(h => h.Update());

                foreach (var storage in storageHardwares)
                {
                    var tempSensor = storage.Sensors?
                        .FirstOrDefault(s => s.SensorType == SensorType.Temperature);
                    if (tempSensor?.Value is float value && value > 0)
                    {
                        temps.Add(value);
                    }
                }
            }
            catch (ArgumentOutOfRangeException ex)
            {
                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"SSD temperature read error: {ex.Message}");
                }

                return (0, 0);
            }

            switch (temps.Count)
            {
                case 0:
                    return (0, 0);
                case 1:
                    return (temps[0], 0);
                default:
                    return (temps[0], temps[1]);
            }
        }

        public async Task<float> GetMemoryUsageAsync()
        {
            if (!await IsSupportedAsync().ConfigureAwait(false))
            {
                return 0;
            }

            _memoryHardware?.Update();

            if (_memoryHardware == null) return 0;

            return _memoryHardware.Sensors?
              .FirstOrDefault(s => s.SensorType == SensorType.Load)?
              .Value ?? 0;
        }

        public async Task<double> GetHighestMemoryTemperatureAsync()
        {
            if (!await IsSupportedAsync().ConfigureAwait(false))
            {
                return 0;
            }

            var memoryHardwares = _interestedHardwares
              .Where(h => h.HardwareType == HardwareType.Memory);

            if (memoryHardwares == null || !memoryHardwares.Any()) return 0;

            float maxTemperature = 0;
            foreach (var memoryHardware in memoryHardwares)
            {
                memoryHardware.Update();
                if (memoryHardware.Sensors == null) continue;

                foreach (var sensor in memoryHardware.Sensors)
                {
                    if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue && sensor.Value > 0)
                    {
                        if (sensor.Value.Value > maxTemperature)
                        {
                            maxTemperature = sensor.Value.Value;
                        }
                    }
                }
            }
            return maxTemperature;
        }

        private async Task<uint> InitializeAsync()
        {
            if (_initialized) return 2;

            await _initSemaphore.WaitAsync();
            try
            {
                if (_initialized) return 2;

                await Task.Run(() => GetInterestedHardwares()).ConfigureAwait(false);
                return 1;
            }
            catch (DllNotFoundException)
            {
                var settings = IoCContainer.Resolve<ApplicationSettings>();
                settings.Store.UseNewSensorDashboard = false;
                settings.SynchronizeStore();
                return 0;
            }
            finally
            {
                _initSemaphore.Release();
                _initialized = true;
            }
        }

        public async void NeedRefreshHardware(string hardware)
        {
            if (!await IsSupportedAsync().ConfigureAwait(false))
            {
                return;
            }

            if (_computer == null)
            {
                return;
            }

            if (hardware == "NvidiaGPU")
            {
                _gpuHardware = null;

                _computer.Open();
                _computer.Accept(new UpdateVisitor());
                _computer.Reset();
                _interestedHardwares.Clear();
                _interestedHardwares.AddRange(_computer.Hardware);
                _gpuHardware = _interestedHardwares.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia);

                _needRefreshGpuHardware = true;
            }
        }

        private string StripName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "UNKNOWN";

            string cleanedName = name.Trim();

            if (cleanedName.Contains("AMD", StringComparison.OrdinalIgnoreCase))
            {
                cleanedName = Regex.Replace(cleanedName, @"\s+with\s+Radeon\s+Graphics$", "",
                                            RegexOptions.IgnoreCase);
            }
            else if (cleanedName.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            {
                cleanedName = Regex.Replace(cleanedName, @"\s*\d+(?:th|st|nd|rd)?\s+Gen\b", "",
                                            RegexOptions.IgnoreCase);
            }
            else if (cleanedName.Contains("Nvidia", StringComparison.OrdinalIgnoreCase) ||
                     cleanedName.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(cleanedName,
                    @"(?i)\b(?:Nvidia\s+)?(GeForce\s+(?:RTX|GTX)\s+\d{3,4}(?:\s+(Ti|SUPER|Ti\s+SUPER|M))?)\b(?:\s+Laptop\s+GPU)?(?!\S)");
                if (match.Success)
                {
                    cleanedName = match.Groups[1].Value;
                }
            }

            return Regex.Replace(cleanedName, @"\s+", " ").Trim();
        }

        // Test methods to fix RTX 40 Series sensor issue.
        //public string GetThermalSensors()
        //{
        //    NvApi.Reinitialize();
        //    NvPhysicalGpuHandle[] handles = new NvPhysicalGpuHandle[NvApi.MAX_PHYSICAL_GPUS];
        //    int count;
        //    NvApi.NvAPI_EnumPhysicalGPUs(handles, out count);
        //    StringBuilder values = new StringBuilder();
        //    var thermalSensorsMask = 0u;
        //    bool hasAnyThermalSensor = false;

        //    foreach (var handle in handles)
        //    {
        //        for (int thermalSensorsMaxBit = 0; thermalSensorsMaxBit < 32; thermalSensorsMaxBit++)
        //        {
        //            // Find the maximum thermal sensor mask value.
        //            thermalSensorsMask = 1u << thermalSensorsMaxBit;

        //            GetThermalSensors(thermalSensorsMask, out NvApi.NvStatus thermalSensorsStatus, handle);
        //            if (thermalSensorsStatus == NvApi.NvStatus.OK)
        //            {
        //                hasAnyThermalSensor = true;
        //                continue;
        //            }

        //            thermalSensorsMask--;
        //            break;
        //        }

        //        if (thermalSensorsMask > 0)
        //        {
        //            NvApi.NvThermalSensors nvThermalSensors = GetThermalSensors(thermalSensorsMask, out NvApi.NvStatus status, handle);
        //            if (status == NvApi.NvStatus.OK)
        //            {
        //                int i = 0;
        //                foreach (var item in nvThermalSensors.Temperatures)
        //                {
        //                    ++i;
        //                    values.AppendLine($"{handle.GetHashCode()} {i} {item / 256.0f}");
        //                }
        //            }
        //        }
        //    }

        //    return values.ToString();
        //}

        //private NvApi.NvThermalSensors GetThermalSensors(uint mask, out NvApi.NvStatus status, NvApi.NvPhysicalGpuHandle handle)
        //{
        //    if (NvApi.NvAPI_GPU_ThermalGetSensors == null)
        //    {
        //        status = NvApi.NvStatus.Error;
        //        return default;
        //    }

        //    var thermalSensors = new NvApi.NvThermalSensors
        //    {
        //        Version = (uint)NvApi.MAKE_NVAPI_VERSION<NvApi.NvThermalSensors>(2),
        //        Mask = mask
        //    };

        //    status = NvApi.NvAPI_GPU_ThermalGetSensors(handle, ref thermalSensors);
        //    return status == NvApi.NvStatus.OK ? thermalSensors : default;
        //}

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}