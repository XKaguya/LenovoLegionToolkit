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
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);
    private readonly List<IHardware> _hardware = new();

    private Computer? _computer;
    private IHardware? _cpuHardware;
    private IHardware? _amdGpuHardware;
    private IHardware? _gpuHardware;
    private IHardware? _memoryHardware;

    private bool _needRefreshGpuHardware;
    private string _cachedCpuName = string.Empty;
    private string _cachedGpuName = string.Empty;
    private float _cachedCpuPower;
    private int _cachedCpuPowerTime;

    private readonly Lock _hardwareLock = new();
    private volatile bool _hardwareInitialized;

    private readonly GPUController _gpuController = IoCContainer.Resolve<GPUController>();

    public async Task<LibreHardwareMonitorInitialState> IsSupportedAsync()
    {
        LibreHardwareMonitorInitialState result = await InitializeAsync();

        try
        {
            bool haveHardware = _hardware.Count != 0;

            if (haveHardware && result == LibreHardwareMonitorInitialState.Initialized || result == LibreHardwareMonitorInitialState.Success)
            {
                return result;
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Sensor group check failed: {ex}");
            return result;
        }

        return LibreHardwareMonitorInitialState.Fail;
    }

    private void GetHardware()
    {
        lock (_hardwareLock)
        {
            if (_hardwareInitialized) return;

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

                foreach (var hardware in _computer.Hardware)
                {
                    Log.Instance.Trace($"Detected hardware: {hardware.HardwareType} - {hardware.Name}");
                }

                _hardware.AddRange(_computer.Hardware);
                _cpuHardware = _hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);

                // The GPU Dashboard card was designed for discrete graphic cards. So we don't pick Intel & Amd's integrated GPUs here.
                _amdGpuHardware = _hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuAmd && !Regex.IsMatch(h.Name, @"AMD Radeon\(TM\)\s+\d+M", RegexOptions.IgnoreCase));
                _gpuHardware = _hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia);
                _memoryHardware = _hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory && h.Name == "Total Memory");
            }
            finally
            {
                _hardwareInitialized = true;
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

        if (!string.IsNullOrEmpty(_cachedGpuName) && !_needRefreshGpuHardware)
        {
            return Task.FromResult(_cachedGpuName);
        }

        var gpu = _gpuHardware ?? _amdGpuHardware;
        _cachedGpuName = gpu != null ? StripName(gpu.Name) : "UNKNOWN";
        _needRefreshGpuHardware = false;

        return Task.FromResult(_cachedGpuName);
    }

    public Task<float> GetCpuPowerAsync()
    {
        const float maxValidPower = 400f;
        const float invalidPower = -1f;

        try
        {
            if (!IsLibreHardwareMonitorInitialized() || _cpuHardware == null)
            {
                return Task.FromResult(invalidPower);
            }

            var sensor = _cpuHardware.Sensors?.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name.Contains("Package"));
            var powerValue = sensor?.Value;

            if (powerValue is null or <= 0)
            {
                return Task.FromResult(invalidPower);
            }

            if (powerValue > maxValidPower)
            {
                ResetSensors();
                return Task.FromResult(invalidPower);
            }

            var power = powerValue.Value;

            // Can't exactly same for 10 times.
            if (power.Equals(_cachedCpuPower))
            {
                if (_cachedCpuPowerTime >= 10)
                {
                    ResetSensors();
                    Log.Instance.Trace($"Detected CPU Power invalid for serval times, Resetting sensors...");
                    return Task.FromResult(invalidPower);
                }
                else
                {
                    ++_cachedCpuPowerTime;
                }
            }
            else
            {
                _cachedCpuPower = power;
                _cachedCpuPowerTime = 0;
            }

            return Task.FromResult(power);
        }
        catch (Exception)
        {
            return Task.FromResult(invalidPower);
        }
    }

    public async Task<float> GetGpuPowerAsync()
    {
        if (!IsLibreHardwareMonitorInitialized())
        {
            return -1;
        }

        var state = await _gpuController.GetLastKnownStateAsync().ConfigureAwait(false);
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
            Log.Instance.Trace($"GetGpuPowerAsync() raised exception: ", ex);

            return -1;
        }
    }

    public async Task<float> GetGpuVramTemperatureAsync()
    {
        if (!IsLibreHardwareMonitorInitialized())
        {
            return 0;
        }

        var gpuState = await _gpuController.GetLastKnownStateAsync().ConfigureAwait(false);
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

    public Task<(float, float)> GetSsdTemperaturesAsync()
    {
        if (!IsLibreHardwareMonitorInitialized())
        {
            return Task.FromResult((0f, 0f));
        }

        var temps = new List<float>();

        try
        {
            var storageHardware = _hardware.Where(h => h.HardwareType == HardwareType.Storage).ToList();

            if (storageHardware.Count == 0)
            {
                return Task.FromResult((0f, 0f));
            }

            foreach (var storage in storageHardware)
            {
                var tempSensor = storage.Sensors?.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
                if (tempSensor is { SensorType: SensorType.Temperature, Value: > 0 })
                {
                    temps.Add(tempSensor.Value.Value);
                }
            }
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Log.Instance.Trace($"SSD temperature read error: {ex.Message}");

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

        var memoryHardware = _hardware.Where(h => h.HardwareType == HardwareType.Memory).ToList(); // 或者 .ToArray()

        if (memoryHardware.Count == 0)
        {
            return Task.FromResult(0.0);
        }

        float maxTemperature = 0;
        foreach (var hardware in memoryHardware)
        {
            if (hardware.Sensors == null)
            {
                continue;
            }

            foreach (var sensor in hardware.Sensors)
            {
                if (sensor is { SensorType: SensorType.Temperature, Value: > 0 })
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

    private async Task<LibreHardwareMonitorInitialState> InitializeAsync()
    {
        if (_initialized)
        {
            InitialState = LibreHardwareMonitorInitialState.Initialized;
            return InitialState;
        }

        await _initSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                InitialState = LibreHardwareMonitorInitialState.Initialized;
                return InitialState;
            }

            await Task.Run(GetHardware).ConfigureAwait(false);
            _initialized = true;

            if (_hardware.Count == 0)
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
            var msg = $"LibreHardwareMonitor initialization failed. Disabling new sensor dashboard. [type={GetType().Name}]";
            Log.Instance.Trace($"{msg}");
            throw new Exception(msg, ex);
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

        if (hardware != "NvidiaGPU")
        {
            return;
        }

        _gpuHardware = null;

        ResetSensors();
        _hardware.Clear();
        _hardware.AddRange(_computer.Hardware);
        _gpuHardware = _hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia);

        _needRefreshGpuHardware = true;
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
                if (_computer == null || !_hardwareInitialized)
                {
                    return;
                }

                try
                {
                    foreach (var hardware in _hardware)
                    {
                        hardware?.Update();
                    }
                }
                catch (AccessViolationException)
                {

                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Failed to update sensors: {ex.Message}", ex);
                }
            }
        }).ConfigureAwait(false);
    }

    #region Helper
    private void ResetSensors()
    {
        _computer?.Close();
        _computer?.Open();
        _computer?.Accept(new UpdateVisitor());
        _computer?.Reset();
    }

    private static string StripName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "UNKNOWN";
        }

        var cleanedName = name.Trim();

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

    public bool IsGpuInActive(GPUState state)
    {
        return state is GPUState.Inactive or GPUState.PoweredOff or GPUState.Unknown or GPUState.NvidiaGpuNotFound;
    }

    public bool IsLibreHardwareMonitorInitialized()
    {
        return InitialState is LibreHardwareMonitorInitialState.Initialized or LibreHardwareMonitorInitialState.Success;
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

        return Directory.Exists(pawnIoPath);
    }
    #endregion

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}