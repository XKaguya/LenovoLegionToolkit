// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) RAMSPDToolkit and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using LenovoLegionToolkit.Lib.Utils;
using RAMSPDToolkit.I2CSMBus;
using RAMSPDToolkit.SPD;
using RAMSPDToolkit.SPD.Interfaces;
using RAMSPDToolkit.SPD.Interop.Shared;
using RAMSPDToolkit.Windows.Driver;
using RAMSPDToolkit.Windows.Driver.Implementations;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors
{
    public class MemorySensorController : IDisposable
    {
        private bool _initialized;
        private readonly List<IThermalSensor> _memorySensors = new();
        private bool _driverLoaded;

        public async Task<bool> IsSupportedAsync()
        {
            try
            {
                await InitializeAsync().ConfigureAwait(false);
                return _memorySensors.Count > 0;
            }
            catch (Exception ex)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Memory sensor check failed: {ex}");
                return false;
            }
        }

        public async Task<double> GetHighestMemoryTemperature()
        {
            if (!await IsSupportedAsync().ConfigureAwait(false))
                return 0;

            double maxTemp = 0;
            bool anySuccess = false;

            foreach (var sensor in _memorySensors)
            {
                try
                {
                    if (sensor.UpdateTemperature())
                    {
                        maxTemp = Math.Max(maxTemp, sensor.Temperature);
                        anySuccess = true;
                    }
                }
                catch (Exception ex) when (LogException(ex))
                {
                }
            }

            return anySuccess ? maxTemp : 0;
        }

        private async Task InitializeAsync()
        {
            if (_initialized) return;

            try
            {
                await Task.Run(() =>
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

                    foreach (var bus in SMBusManager.RegisteredSMBuses)
                    {
                        Parallel.For(SPDConstants.SPD_BEGIN, SPDConstants.SPD_END + 1, i =>
                        {
                            var detector = new SPDDetector(bus, (byte)i);
                            if (detector.IsValid && detector.Accessor is IThermalSensor ts)
                            {
                                lock (_memorySensors)
                                {
                                    _memorySensors.Add(ts);
                                }
                                if (Log.Instance.IsTraceEnabled)
                                    Log.Instance.Trace($"Detected memory sensor on bus {bus} slot {i}");
                            }
                        });
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                _initialized = true;
            }
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