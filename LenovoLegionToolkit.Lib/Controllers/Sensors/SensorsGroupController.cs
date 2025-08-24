// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) RAMSPDToolkit and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using LenovoLegionToolkit.Lib.Utils;
using LibreHardwareMonitor.Hardware;
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
        private IHardware _cpuHardware;
        private IHardware _gpuHardware;
        private IHardware _memoryHardware;

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
                    _gpuHardware = _interestedHardwares.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia || h.HardwareType == HardwareType.GpuAmd);
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

            return StripName(_cpuHardware.Name, "Intel(R)", "Core(TM)", "AMD", "Ryzen", "Processor", "CPU", "Gen");
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

            return StripName(_gpuHardware.Name, "NVIDIA", "AMD", "LAPTOP", "GPU");
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

        private string StripName(string name, params string[] terms)
        {
            if (string.IsNullOrEmpty(name)) return "UNKNOWN";

            var sb = new StringBuilder(name);
            string cleanedName = Regex.Replace(sb.ToString(), @"\s*\d+(?:th|st|nd|rd)?\s+Gen\b", string.Empty, RegexOptions.IgnoreCase);
            sb = new StringBuilder(cleanedName);
            foreach (var term in terms)
            {
                int index;
                while ((index = sb.ToString().IndexOf(term, StringComparison.OrdinalIgnoreCase)) != -1)
                {
                    sb.Remove(index, term.Length);
                }
            }

            return sb.ToString().Replace("  ", " ").Trim();
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}