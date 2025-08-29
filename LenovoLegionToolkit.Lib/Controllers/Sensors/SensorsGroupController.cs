// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) RAMSPDToolkit and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LibreHardwareMonitor.Hardware;
using NvAPIWrapper.GPU;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors
{
    public class SensorsGroupController : IDisposable
    {
        private bool _initialized;
        private readonly SemaphoreSlim _initSemaphore = new SemaphoreSlim(1, 1);
        private readonly List<IHardware> _interestedHardwares = new();
        private IHardware? _cpuHardware;
        private IHardware? _gpuHardware;
        private IHardware? _memoryHardware;
        private PhysicalGPU? _gpuHardwareNVAPI;
        private GPUThermalSensor? _gpuThermalSensor;

        private string _cachedCpuName = string.Empty;
        private string _cachedGpuName = string.Empty;

        private readonly object _hardwareLock = new object();
        private volatile bool _hardwaresInitialized;

        public async Task<bool> IsSupportedAsync()
        {
            try
            {
                await InitializeAsync();
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
                    var computer = new Computer
                    {
                        IsCpuEnabled = true,
                        IsGpuEnabled = true,
                        IsMemoryEnabled = true,
                        IsMotherboardEnabled = false,
                        IsControllerEnabled = false,
                        IsNetworkEnabled = false,
                        IsStorageEnabled = true
                    };

                    computer.Open();
                    computer.Accept(new UpdateVisitor());

                    if (Log.Instance.IsTraceEnabled)
                    {
                        foreach (var hardware in computer.Hardware)
                        {
                            Log.Instance.Trace($"Detected hardware: {hardware.HardwareType} - {hardware.Name}");
                        }
                    }

                    _interestedHardwares.AddRange(computer.Hardware);
                    _cpuHardware = _interestedHardwares.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
                    _gpuHardware = _interestedHardwares.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia);
                    _memoryHardware = _interestedHardwares.FirstOrDefault(h => h.HardwareType == HardwareType.Memory);
                    _gpuHardwareNVAPI = NVAPI.GetGPU();
                    _gpuThermalSensor = _gpuHardwareNVAPI.ThermalInformation.ThermalSensors.FirstOrDefault(s => s.Target.HasFlag(NvAPIWrapper.Native.GPU.ThermalSettingsTarget.Memory));
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

            if (_gpuHardware == null)
            {
                return "UNKNOWN";
            }

            if (!string.IsNullOrEmpty(_cachedGpuName))
            {
                return _cachedGpuName;
            }

            _cachedGpuName = StripName(_gpuHardware.Name);
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
                return 0;
            }

            if (_gpuHardware == null)
            {
                return 0;
            }

            _gpuHardware.Update();

            var sensor = _gpuHardware.Sensors?
              .FirstOrDefault(s => s.SensorType == SensorType.Power);
            return sensor?.Value ?? 0;
        }

        public async Task<float> GetGpuVramTemperatureAsync()
        {
            throw new NotImplementedException();

            /*if (!await IsSupportedAsync().ConfigureAwait(false))
            {
                return 0;
            }

            if (_gpuThermalSensor != null)
            {
                try
                {
                    return _gpuThermalSensor.CurrentTemperature;
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"GPU VRAM temperature read error: {ex.Message}");
                    return 0;
                }
            }

            if (_gpuHardwareNVAPI == null)
            {
                try
                {
                    _gpuHardwareNVAPI = NVAPI.GetGPU();
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"NVAPI initialization error: {ex.Message}");
                    return 0;
                }
            }

            if (_gpuHardwareNVAPI == null)
            {
                Log.Instance.Trace($"No NVIDIA GPU found via NVAPI.");
                return 0;
            }

            try
            {
                _gpuThermalSensor = _gpuHardwareNVAPI.ThermalInformation.ThermalSensors.FirstOrDefault(s => s.Target.HasFlag(NvAPIWrapper.Native.GPU.ThermalSettingsTarget.Memory));
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Error finding VRAM temperature sensor: {ex.Message}");
            }

            return 0;*/
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

        private async Task InitializeAsync()
        {
            if (_initialized) return;

            await _initSemaphore.WaitAsync();
            try
            {
                if (_initialized) return;

                await Task.Run(() => GetInterestedHardwares()).ConfigureAwait(false);
            }
            finally
            {
                _initSemaphore.Release();
                _initialized = true;
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

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}