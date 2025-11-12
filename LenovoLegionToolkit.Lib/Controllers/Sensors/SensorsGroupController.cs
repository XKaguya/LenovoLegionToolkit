// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) RAMSPDToolkit and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors;

public class SensorsGroupController : IDisposable
{
    private bool _initialized;
    public LibreHardwareMonitorInitialState InitialState { get; private set; }
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

    public async Task<LibreHardwareMonitorInitialState> IsSupportedAsync()
    {
        LibreHardwareMonitorInitialState result = await InitializeAsync();

        try
        {
            bool haveHardware = _interestedHardwares.Count != 0;

            if (haveHardware && result == LibreHardwareMonitorInitialState.Initialized || result == LibreHardwareMonitorInitialState.Success)
            {
                return result;
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
            {
                Log.Instance.Trace($"Sensor group check failed: {ex}");
            }
            return result;
        }

        return LibreHardwareMonitorInitialState.Fail;
    }

    public bool IsLibreHardwareMonitorInitialized()
    {
        return InitialState == LibreHardwareMonitorInitialState.Initialized || InitialState == LibreHardwareMonitorInitialState.Success;
    }


    public bool IsPawnIOInnstalled()
    {
        string? pawnIoPath = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO", "InstallLocation", null) as string;

        if (string.IsNullOrEmpty(pawnIoPath))
        {
            pawnIoPath = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\PawnIO", "Install_Dir", null) as string;
        }

        if (string.IsNullOrEmpty(pawnIoPath))
        {
            pawnIoPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO");
        }

        if (Directory.Exists(pawnIoPath))
        {
            return true;
        }

        return false;
    }

    private void GetInterestedHardwares()
    {
        lock (_hardwareLock)
        {
            if (_hardwaresInitialized) return;

            if (!IsPawnIOInnstalled())
            {
                return;
            }

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

                // The GPU Dashboard card was designed for discrete graphic cards. So we don't pick Intel & Amd's integrated GPUs here.
                _amdGpuHardware = _interestedHardwares.FirstOrDefault(h => h.HardwareType == HardwareType.GpuAmd && !Regex.IsMatch(h.Name, @"AMD Radeon\(TM\)\s+\d+M", RegexOptions.IgnoreCase));
                _gpuHardware = _interestedHardwares.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia);
                _memoryHardware = _interestedHardwares.FirstOrDefault(h => h.HardwareType == HardwareType.Memory && h.Name == "Total Memory");
            }
            finally
            {
                _hardwaresInitialized = true;
            }
        }
    }

    public Task<string> GetCpuNameAsync()
    {
        if (!IsLibreHardwareMonitorInitialized())
        {
            return Task.FromResult("UNKNOWN");
        }

        if (_cpuHardware == null)
        {
            return Task.FromResult("UNKNOWN");
        }

        if (!string.IsNullOrEmpty(_cachedCpuName))
        {
            return Task.FromResult(_cachedCpuName);
        }

        _cachedCpuName = StripName(_cpuHardware.Name);
        return Task.FromResult(_cachedCpuName);
    }

    public Task<string> GetGpuNameAsync()
    {
        if (!IsLibreHardwareMonitorInitialized())
        {
            return Task.FromResult("UNKNOWN");
        }

        if (string.IsNullOrEmpty(_cachedGpuName) || _needRefreshGpuHardware)
        {
            var gpu = _gpuHardware ?? _amdGpuHardware;
            _cachedGpuName = gpu != null ? StripName(gpu.Name) : "UNKNOWN";
            _needRefreshGpuHardware = false;
        }

        return Task.FromResult(_cachedGpuName);
    }

    public Task<float> GetCpuPowerAsync()
    {
        const float MaxValidPower = 400f;
        const float InvalidPower = -1f;

        try
        {
            if (!IsLibreHardwareMonitorInitialized() || _cpuHardware == null)
            {
                return Task.FromResult(InvalidPower);
            }

            var sensor = _cpuHardware.Sensors?.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name == "Package");
            var powerValue = sensor?.Value;

            if (!powerValue.HasValue || powerValue < 0)
            {
                return Task.FromResult(InvalidPower);
            }

            if (powerValue > MaxValidPower)
            {
                _computer?.Open();
                _computer?.Accept(new UpdateVisitor());
                _computer?.Reset();
                return Task.FromResult(InvalidPower);
            }

            return Task.FromResult(powerValue.Value);
        }
        catch (Exception)
        {
            return Task.FromResult(InvalidPower);
        }
    }

    public async Task<float> GetGpuPowerAsync()
    {
        if (!IsLibreHardwareMonitorInitialized())
        {
            return -1;
        }

        var state = await _gpuController.GetLastKnownStateAsync();
        if (_lastGpuPower <= 10 && IsGpuInActive(state))
        {
            return -1;
        }

        if (_gpuHardware == null)
        {
            return -1;
        }

        try
        {
            var sensor = _gpuHardware.Sensors?.FirstOrDefault(s => s.SensorType == SensorType.Power);
            _lastGpuPower = sensor?.Value ?? 0;
            return _lastGpuPower;
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
        if (!IsLibreHardwareMonitorInitialized())
        {
            return 0;
        }

        var gpuState = await _gpuController.GetLastKnownStateAsync();
        if (_lastGpuPower <= 10 && (gpuState == GPUState.Inactive || gpuState == GPUState.PoweredOff))
        {
            return 0;
        }

        if (_gpuHardware == null)
        {
            return 0;
        }

        var sensor = _gpuHardware.Sensors?.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("GPU Memory Junction", StringComparison.OrdinalIgnoreCase));
        return sensor?.Value ?? 0;
    }

    public Task<(float, float)> GetSSDTemperaturesAsync()
    {
        if (!IsLibreHardwareMonitorInitialized())
        {
            return Task.FromResult((0f, 0f));
        }

        var temps = new List<float>();

        try
        {
            var storageHardwares = _interestedHardwares.Where(h => h.HardwareType == HardwareType.Storage).ToList();

            if (storageHardwares.Count == 0)
            {
                return Task.FromResult((0f, 0f));
            }

            foreach (var storage in storageHardwares)
            {
                var tempSensor = storage.Sensors?.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
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

            return Task.FromResult((0f, 0f));
        }

        switch (temps.Count)
        {
            case 0: return Task.FromResult((0f, 0f));
            case 1: return Task.FromResult((temps[0], 0f));
            default: return Task.FromResult((temps[0], temps[1]));
        }
    }

    public Task<float> GetMemoryUsageAsync()
    {
        if (!IsLibreHardwareMonitorInitialized() || _memoryHardware == null)
        {
            return Task.FromResult(0f);
        }

        return Task.FromResult(_memoryHardware.Sensors?.FirstOrDefault(s => s.SensorType == SensorType.Load)?.Value ?? 0);
    }

    public Task<double> GetHighestMemoryTemperatureAsync()
    {
        if (!IsLibreHardwareMonitorInitialized())
        {
            return Task.FromResult(0.0);
        }

        var memoryHardwares = _interestedHardwares.Where(h => h.HardwareType == HardwareType.Memory);

        if (memoryHardwares == null || !memoryHardwares.Any()) return Task.FromResult(0.0);

        float maxTemperature = 0;
        foreach (var memoryHardware in memoryHardwares)
        {
            if (memoryHardware.Sensors == null)
            {
                continue;
            }

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
        return Task.FromResult((double)maxTemperature);
    }

    public bool IsGpuInActive(GPUState state)
    {
        return state == GPUState.Inactive ||
            state == GPUState.PoweredOff ||
            state == GPUState.Unknown ||
            state == GPUState.NvidiaGpuNotFound;
    }

    private async Task<LibreHardwareMonitorInitialState> InitializeAsync()
    {
        if (_initialized)
        {
            InitialState = LibreHardwareMonitorInitialState.Initialized;
            return InitialState;
        }

        await _initSemaphore.WaitAsync();
        try
        {
            if (_initialized)
            {
                InitialState = LibreHardwareMonitorInitialState.Initialized;
                return InitialState;
            }

            await Task.Run(() => GetInterestedHardwares()).ConfigureAwait(false);
            _initialized = true;

            if (_interestedHardwares.Count == 0)
            {
                InitialState = LibreHardwareMonitorInitialState.Fail;
                return InitialState;
            }

            InitialState = LibreHardwareMonitorInitialState.Success;
            return InitialState;
        }
        catch (DllNotFoundException)
        {
            var settings = IoCContainer.Resolve<ApplicationSettings>();
            settings.Store.UseNewSensorDashboard = false;
            settings.SynchronizeStore();
            InitialState = LibreHardwareMonitorInitialState.PawnIONotInstalled;
            return InitialState;
        }
        catch (Exception ex)
        {
            var settings = IoCContainer.Resolve<ApplicationSettings>();
            settings.Store.UseNewSensorDashboard = false;
            settings.SynchronizeStore();
            InitialState = LibreHardwareMonitorInitialState.Fail;
            if (Log.Instance.IsTraceEnabled)
            {
                string msg = $"LibreHardwareMonitor initialization failed. Disabling new sensor dashboard. [type={GetType().Name}]";
                Log.Instance.Trace($"{msg}");
                throw new Exception(msg, ex);
            }
            return InitialState;
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    public void NeedRefreshHardware(string hardware)
    {
        if (!IsLibreHardwareMonitorInitialized())
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
        {
            return "UNKNOWN";
        }

        string cleanedName = name.Trim();

        if (cleanedName.Contains("AMD", StringComparison.OrdinalIgnoreCase))
        {
            cleanedName = Regex.Replace(cleanedName, @"\s+with\s+Radeon\s+Graphics$", "", RegexOptions.IgnoreCase);
        }
        else if (cleanedName.Contains("Intel", StringComparison.OrdinalIgnoreCase))
        {
            cleanedName = Regex.Replace(cleanedName, @"\s*\d+(?:th|st|nd|rd)?\s+Gen\b", "", RegexOptions.IgnoreCase);
        }
        else if (cleanedName.Contains("Nvidia", StringComparison.OrdinalIgnoreCase) || cleanedName.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(cleanedName, @"(?i)\b(?:Nvidia\s+)?(GeForce\s+(?:RTX|GTX)\s+\d{3,4}(?:\s+(Ti|SUPER|Ti\s+SUPER|M))?)\b(?:\s+Laptop\s+GPU)?(?!\S)");
            if (match.Success)
            {
                cleanedName = match.Groups[1].Value;
            }
        }

        return Regex.Replace(cleanedName, @"\s+", " ").Trim();
    }

    public async Task UpdateAsync()
    {
        if (!IsLibreHardwareMonitorInitialized())
        {
            return;
        }

        await Task.Run(() =>
        {
            lock (_hardwareLock)
            {
                if (_computer == null || !_hardwaresInitialized)
                {
                    return;
                }

                try
                {
                    foreach (var hardware in _interestedHardwares)
                    {
                        hardware?.Update();
                    }
                }
                catch (AccessViolationException)
                {

                }
                catch (Exception ex)
                {
                    if (Log.Instance.IsTraceEnabled)
                    {
                        Log.Instance.Trace($"Failed to update sensors: {ex.Message}", ex);
                    }
                }
            }
        }).ConfigureAwait(false);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}