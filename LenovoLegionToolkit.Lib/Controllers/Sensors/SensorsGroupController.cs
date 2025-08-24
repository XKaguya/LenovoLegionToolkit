// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) RAMSPDToolkit and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using LenovoLegionToolkit.Lib.Utils;
using LibreHardwareMonitor.Hardware;
using RAMSPDToolkit.I2CSMBus;
using RAMSPDToolkit.SPD;
using RAMSPDToolkit.SPD.Interfaces;
using RAMSPDToolkit.SPD.Interop.Shared;
using RAMSPDToolkit.Windows.Driver;
using RAMSPDToolkit.Windows.Driver.Implementations;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors
{
    public class SensorsGroupController : IDisposable
    {
        private bool _initialized;
        private readonly List<IThermalSensor> _memorySensors = new();
        private readonly List<IHardware> _interestedHardwares = new();
        private bool _driverLoaded;

        private readonly object _initLock = new object();
        private readonly object _hardwareLock = new object();
        private volatile bool _hardwaresInitialized;

        public async Task<bool> IsSupportedAsync()
        {
            try
            {
                await InitializeAsync();
                await GetInterestedHardwaresAsync();
                return _memorySensors.Count > 0 || _interestedHardwares.Any();
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

                    _interestedHardwares.AddRange(computer.Hardware);
                }
                finally
                {
                    _hardwaresInitialized = true;
                }
            }
        }

        public async Task<float> GetCpuPowerAsync()
        {
            if (!await IsSupportedAsync().ConfigureAwait(false))
            {
                return 0;
            }

            var cpuHardware = _interestedHardwares
              .FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);

            var sensor = cpuHardware?.Sensors?
              .FirstOrDefault(s => s.SensorType == SensorType.Power);

            return sensor?.Value ?? 0;
        }

        public async Task<float> GetGpuPowerAsync()
        {
            if (!await IsSupportedAsync().ConfigureAwait(false))
            {
                return 0;
            }
            var gpuHardware = _interestedHardwares
              .FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia || h.HardwareType == HardwareType.GpuAmd);
            var sensor = gpuHardware?.Sensors?
              .FirstOrDefault(s => s.SensorType == SensorType.Power);
            return sensor?.Value ?? 0;
        }

        public async Task<(float, float)> GetSSDTemperatures()
        {
            if (!await IsSupportedAsync().ConfigureAwait(false))
            {
                return (0, 0);
            }

            var storageHardwares = _interestedHardwares
              .Where(h => h.HardwareType == HardwareType.Storage)
              .ToList();

            if (storageHardwares.Count == 0)
                return (0, 0);

            var temps = new List<float>();

            foreach (var storage in storageHardwares)
            {
                var tempSensor = storage.Sensors?
                  .FirstOrDefault(s => s.SensorType == SensorType.Temperature);
                if (tempSensor?.Value is float value and > 0)
                {
                    temps.Add(value);
                }
            }

            if (temps.Count == 0)
                return (0, 0);

            return (temps[0], temps[1]);
        }

        public async Task<float> GetMemoryUsage()
        {
            if (!await IsSupportedAsync().ConfigureAwait(false))
            {
                return 0;
            }

            var memoryHardware = _interestedHardwares
              .FirstOrDefault(h => h.HardwareType == HardwareType.Memory);

            if (memoryHardware == null) return 0;

            return memoryHardware.Sensors?
              .FirstOrDefault(s => s.SensorType == SensorType.Load)?
              .Value ?? 0;
        }

        public async Task<double> GetHighestMemoryTemperature()
        {
            if (!await IsSupportedAsync().ConfigureAwait(false))
            {
                return 0;
            }

            if (_memorySensors.Count == 0)
            {
                await GetInterestedHardwaresAsync().ConfigureAwait(false);
                var memoryHardware = _interestedHardwares
                  .FirstOrDefault(h => h.HardwareType == HardwareType.Memory);

                if (memoryHardware != null)
                {
                    var tempSensor = memoryHardware.Sensors?
                      .FirstOrDefault(s => s.SensorType == SensorType.Temperature);

                    return tempSensor?.Value ?? 0;
                }

                return 0;
            }

            double maxTemp = 0;
            bool anySuccess = false;

            Parallel.ForEach(_memorySensors, sensor =>
            {
                try
                {
                    if (sensor.UpdateTemperature())
                    {
                        lock (_memorySensors)
                        {
                            maxTemp = Math.Max(maxTemp, sensor.Temperature);
                        }
                        anySuccess = true;
                    }
                }
                catch (Exception ex) when (LogException(ex))
                {
                }
            });

            return anySuccess ? maxTemp : 0;
        }

        private async Task InitializeAsync()
        {
            if (_initialized) return;

            lock (_initLock)
            {
                if (_initialized) return;

                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        if (!LoadDriver())
                        {
                            _initialized = true;
                            return;
                        }
                        _driverLoaded = true;
                    }

                    SMBusManager.DetectSMBuses();

                    var detectedSensors = new ConcurrentBag<IThermalSensor>();
                    Parallel.ForEach(SMBusManager.RegisteredSMBuses, bus =>
                    {
                        Parallel.For(SPDConstants.SPD_BEGIN, SPDConstants.SPD_END + 1, i =>
                        {
                            var detector = new SPDDetector(bus, (byte)i);
                            if (detector.IsValid && detector.Accessor is IThermalSensor ts)
                            {
                                detectedSensors.Add(ts);
                                if (Log.Instance.IsTraceEnabled)
                                    Log.Instance.Trace($"Detected memory sensor on bus {bus} slot {i}");
                            }
                        });
                    });
                    _memorySensors.AddRange(detectedSensors);
                }
                finally
                {
                    _initialized = true;
                }
            }
        }
        private async Task GetInterestedHardwaresAsync()
        {
            if (_hardwaresInitialized) return;
            await Task.Run(() => GetInterestedHardwares()).ConfigureAwait(false);
        }

        public bool LoadDriver()
        {
            DriverManager.InitDriver(InternalDriver.OLS);

            if (DriverManager.DriverImplementation != InternalDriver.OLS || !DriverManager.Driver.Load())
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Failed to load driver");

                return false;
            }

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Driver loaded successfully");

            return true;
        }

        public void UnloadDriver()
        {
            try
            {
                DriverManager.Driver?.Unload();
            }
            catch
            {
                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"Failed to unload driver");
                }
            }
        }

        private bool LogException(Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Memory sensor error: {ex.Message}");
            return true;
        }

        public void Dispose()
        {
            if (_driverLoaded && OperatingSystem.IsWindows())
            {
                try
                {
                    DriverManager.Driver?.Unload();
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Driver unloaded");
                }
                catch
                {
                }
            }
            GC.SuppressFinalize(this);
        }
    }
}