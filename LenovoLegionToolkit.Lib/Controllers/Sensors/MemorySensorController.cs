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
    public class MemorySensorController : IDisposable
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
                await InitializeAsync().ConfigureAwait(false);
                GetInterestedHardwares();
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
                        IsMotherboardEnabled = true,
                        IsControllerEnabled = true,
                        IsNetworkEnabled = true,
                        IsStorageEnabled = true
                    };

                    computer.Open();
                    computer.Accept(new UpdateVisitor());

                    var hardwareList = new List<IHardware>(computer.Hardware.Count);

                    foreach (var hardware in computer.Hardware)
                    {
                        if (hardware.HardwareType is HardwareType.Memory or HardwareType.Storage)
                        {
                            hardwareList.Add(hardware);

                            if (Log.Instance.IsTraceEnabled)
                                Log.Instance.Trace($"Detected {hardware.HardwareType}: {hardware.Name}");
                        }
                    }

                    _interestedHardwares.AddRange(hardwareList);
                }
                finally
                {
                    _hardwaresInitialized = true;
                }
            }
        }

        public async Task<(float, float)> GetSSDTemperatures()
        {
            await GetInterestedHardwaresAsync().ConfigureAwait(false);

            var storageHardwares = _interestedHardwares
              .Where(h => h.HardwareType == HardwareType.Storage)
              .ToList();

            if (storageHardwares.Count == 0)
                return (0, 0);

            var temps = new List<float>();

            foreach (var hardware in storageHardwares)
            {
                var sensor = hardware.Sensors?.FirstOrDefault();
                if (sensor != null)
                {
                    temps.Add(sensor.Value ?? 0);
                }
            }

            if (temps.Count == 0) return (0, 0);

            return temps.Count > 1
              ? (MathF.Round(temps[0], 1), MathF.Round(temps[1], 1))
              : (MathF.Round(temps[0], 1), 0);
        }

        public async Task<float> GetMemoryUsage()
        {
            await GetInterestedHardwaresAsync().ConfigureAwait(false);

            var memoryHardware = _interestedHardwares
              .FirstOrDefault(h => h.HardwareType == HardwareType.Memory);

            if (memoryHardware == null) return 0;

            var sensor = memoryHardware.Sensors?.ElementAtOrDefault(2);
            return sensor?.Value ?? 0;
        }

        public async Task<double> GetHighestMemoryTemperature()
        {
            await InitializeAsync().ConfigureAwait(false);

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