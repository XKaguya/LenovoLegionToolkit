// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) RAMSPDToolkit and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System;
using Registry = Microsoft.Win32.Registry;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors;

public class SensorsGroupController : IDisposable
{
    #region Constants (Magic Words & Numbers)

    private const float INVALID_VALUE_FLOAT = -1f;
    private const double INVALID_VALUE_DOUBLE = 0.0;
    private const string UNKNOWN_NAME = "UNKNOWN";

    private const string SENSOR_NAME_TOTAL_MEMORY = "Total Memory";
    private const string SENSOR_NAME_PACKAGE = "Package";
    private const string SENSOR_NAME_GPU_HOTSPOT = "GPU Memory Junction";

    private const string HARDWARE_ID_NVIDIA_GPU = "NvidiaGPU";

    private const string REGEX_AMD_GPU_INTEGRATED = @"AMD Radeon\(TM\)\s+\d+M";
    private const string REGEX_STRIP_AMD = @"\s+with\s+Radeon\s+Graphics$";
    private const string REGEX_STRIP_INTEL = @"\s*\d+(?:th|st|nd|rd)?\s+Gen\b";
    private const string REGEX_STRIP_NVIDIA = @"(?i)\b(?:Nvidia\s+)?(GeForce\s+(?:RTX|GTX)\s+\d{3,4}(?:\s+(Ti|SUPER|Ti\s+SUPER|M))?)\b(?:\s+Laptop\s+GPU)?(?!\S)";
    private const string REGEX_CLEAN_SPACES = @"\s+";

    private const float MAX_VALID_CPU_POWER = 400f;
    private const float MIN_VALID_POWER_READING = 0f;
    private const int MAX_CPU_POWER_STUCK_RETRIES = 10;
    private const float MIN_ACTIVE_GPU_POWER = 10f;

    private const string REG_KEY_PAWN_IO = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
    private const string REG_VAL_INSTALL_LOC = "InstallLocation";
    private const string REG_KEY_PAWN_IO_WOW64 = @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\PawnIO";
    private const string REG_VAL_INSTALL_DIR = "Install_Dir";
    private const string FOLDER_PAWN_IO = "PawnIO";

    #endregion

    private bool _initialized;
    public LibreHardwareMonitorInitialState InitialState { get; private set; }

    private float _lastGpuPower;
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);
    private readonly List<IHardware> _hardware = [];

    private volatile bool _isResetting;

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
        LibreHardwareMonitorInitialState result = await InitializeAsync().ConfigureAwait(false);

        try
        {
            bool haveHardware;
            lock (_hardwareLock)
            {
                haveHardware = _hardware.Count != 0;
            }

            if (haveHardware && result is LibreHardwareMonitorInitialState.Initialized or LibreHardwareMonitorInitialState.Success)
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
                _amdGpuHardware = _hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuAmd && !Regex.IsMatch(h.Name, REGEX_AMD_GPU_INTEGRATED, RegexOptions.IgnoreCase));
                _gpuHardware = _hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia);
                _memoryHardware = _hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory && h.Name == SENSOR_NAME_TOTAL_MEMORY);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"GetHardware failed: {ex}");
                _computer?.Close();
                _computer = null;
                _hardware.Clear();
                throw;
            }
            finally
            {
                _hardwareInitialized = true;
            }
        }
    }

    public Task<string> GetCpuNameAsync()
    {
        if (_isResetting || !IsLibreHardwareMonitorInitialized() || _cpuHardware == null)
        {
            return Task.FromResult(UNKNOWN_NAME);
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
        if (_isResetting || !IsLibreHardwareMonitorInitialized())
        {
            return Task.FromResult(UNKNOWN_NAME);
        }

        if (!string.IsNullOrEmpty(_cachedGpuName) && !_needRefreshGpuHardware)
        {
            return Task.FromResult(_cachedGpuName);
        }

        var gpu = _gpuHardware ?? _amdGpuHardware;
        _cachedGpuName = gpu != null ? StripName(gpu.Name) : UNKNOWN_NAME;
        _needRefreshGpuHardware = false;

        return Task.FromResult(_cachedGpuName);
    }

    public Task<float> GetCpuPowerAsync()
    {
        if (_isResetting) return Task.FromResult(INVALID_VALUE_FLOAT);

        try
        {
            if (!IsLibreHardwareMonitorInitialized() || _cpuHardware == null)
            {
                return Task.FromResult(INVALID_VALUE_FLOAT);
            }

            var sensor = _cpuHardware.Sensors?.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name.Contains(SENSOR_NAME_PACKAGE));
            var powerValue = sensor?.Value;

            switch (powerValue)
            {
                case null or <= MIN_VALID_POWER_READING:
                    return Task.FromResult(INVALID_VALUE_FLOAT);
                case > MAX_VALID_CPU_POWER:
                    Log.Instance.Trace($"CPU Power spike detected ({powerValue}). Resetting sensors.");
                    ResetSensors();
                    _cachedCpuPowerTime = 0;
                    _cachedCpuPower = -1f;
                    return Task.FromResult(INVALID_VALUE_FLOAT);
            }

            var power = powerValue.Value;

            if (Math.Abs(power - _cachedCpuPower) < float.Epsilon)
            {
                if (_cachedCpuPowerTime >= MAX_CPU_POWER_STUCK_RETRIES)
                {
                    Log.Instance.Trace($"Detected CPU Power stuck at {_cachedCpuPower} for serval cycles, Resetting sensors...");
                    ResetSensors();

                    _cachedCpuPowerTime = 0;
                    _cachedCpuPower = -1f;

                    return Task.FromResult(INVALID_VALUE_FLOAT);
                }

                ++_cachedCpuPowerTime;
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
            return Task.FromResult(INVALID_VALUE_FLOAT);
        }
    }

    public async Task<float> GetGpuPowerAsync()
    {
        if (_isResetting || !IsLibreHardwareMonitorInitialized())
        {
            return INVALID_VALUE_FLOAT;
        }

        var state = await _gpuController.GetLastKnownStateAsync().ConfigureAwait(false);

        if ((_lastGpuPower <= MIN_ACTIVE_GPU_POWER && IsGpuInActive(state)) || _gpuHardware == null)
        {
            return INVALID_VALUE_FLOAT;
        }

        try
        {
            var sensor = _gpuHardware.Sensors?.FirstOrDefault(s => s.SensorType == SensorType.Power);
            _lastGpuPower = sensor?.Value ?? 0;
            return _lastGpuPower;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"GetGpuPowerAsync() exception: {ex.Message}");
            return INVALID_VALUE_FLOAT;
        }
    }

    public async Task<float> GetGpuVramTemperatureAsync()
    {
        if (_isResetting || !IsLibreHardwareMonitorInitialized())
        {
            return INVALID_VALUE_FLOAT;
        }

        var gpuState = await _gpuController.GetLastKnownStateAsync().ConfigureAwait(false);
        if ((_lastGpuPower <= MIN_ACTIVE_GPU_POWER && gpuState is GPUState.Inactive or GPUState.PoweredOff) || _gpuHardware == null)
        {
            return INVALID_VALUE_FLOAT;
        }

        var sensor = _gpuHardware.Sensors?.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains(SENSOR_NAME_GPU_HOTSPOT, StringComparison.OrdinalIgnoreCase));
        return sensor?.Value ?? INVALID_VALUE_FLOAT;
    }

    public Task<(float, float)> GetSsdTemperaturesAsync()
    {
        if (_isResetting || !IsLibreHardwareMonitorInitialized())
        {
            return Task.FromResult((INVALID_VALUE_FLOAT, INVALID_VALUE_FLOAT));
        }

        var temps = new List<float>();

        try
        {
            List<IHardware> storageHardware;
            lock (_hardwareLock)
            {
                storageHardware = _hardware.Where(h => h.HardwareType == HardwareType.Storage).ToList();
            }

            if (storageHardware.Count == 0)
            {
                return Task.FromResult((INVALID_VALUE_FLOAT, INVALID_VALUE_FLOAT));
            }

            foreach (var tempSensor in storageHardware.Select(storage => storage.Sensors?.FirstOrDefault(s => s.SensorType == SensorType.Temperature)))
            {
                if (tempSensor is { SensorType: SensorType.Temperature, Value: > 0 })
                {
                    temps.Add(tempSensor.Value.Value);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"SSD temperature read error: {ex.Message}");
            return Task.FromResult((INVALID_VALUE_FLOAT, INVALID_VALUE_FLOAT));
        }

        return temps.Count switch
        {
            0 => Task.FromResult((INVALID_VALUE_FLOAT, INVALID_VALUE_FLOAT)),
            1 => Task.FromResult((temps[0], INVALID_VALUE_FLOAT)),
            _ => Task.FromResult((temps[0], temps[1]))
        };
    }

    public Task<float> GetMemoryUsageAsync()
    {
        if (_isResetting || !IsLibreHardwareMonitorInitialized() || _memoryHardware == null)
        {
            return Task.FromResult(INVALID_VALUE_FLOAT);
        }

        return Task.FromResult(_memoryHardware.Sensors?.FirstOrDefault(s => s.SensorType == SensorType.Load)?.Value ?? 0);
    }

    public Task<double> GetHighestMemoryTemperatureAsync()
    {
        if (_isResetting || !IsLibreHardwareMonitorInitialized())
        {
            return Task.FromResult(INVALID_VALUE_DOUBLE);
        }

        List<IHardware> memoryHardware;
        lock (_hardwareLock)
        {
            memoryHardware = _hardware.Where(h => h.HardwareType == HardwareType.Memory).ToList();
        }

        if (memoryHardware.Count == 0)
        {
            return Task.FromResult(INVALID_VALUE_DOUBLE);
        }

        float maxTemperature = 0;
        foreach (var hardware in memoryHardware)
        {
            if (hardware.Sensors == null) continue;

            foreach (var sensor in hardware.Sensors)
            {
                if (sensor is not { SensorType: SensorType.Temperature, Value: > 0 })
                {
                    continue;
                }
                if (sensor.Value.Value > maxTemperature)
                {
                    maxTemperature = sensor.Value.Value;
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
            HandleInitException("DLL Not Found");
            InitialState = LibreHardwareMonitorInitialState.PawnIONotInstalled;
            return InitialState;
        }
        catch (Exception ex)
        {
            HandleInitException(ex.Message);
            Log.Instance.Trace($"LibreHardwareMonitor initialization failed: {ex}");
            throw;
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    private void HandleInitException(string reason)
    {
        var settings = IoCContainer.Resolve<ApplicationSettings>();
        settings.Store.UseNewSensorDashboard = false;
        settings.SynchronizeStore();
        InitialState = LibreHardwareMonitorInitialState.Fail;
        Log.Instance.Trace($"Disabling new sensor dashboard due to error: {reason}");
    }

    public void NeedRefreshHardware(string hardwareId)
    {
        if (!IsLibreHardwareMonitorInitialized())
        {
            return;
        }

        if (_computer == null)
        {
            return;
        }

        if (hardwareId != HARDWARE_ID_NVIDIA_GPU)
        {
            return;
        }

        lock (_hardwareLock)
        {
            _gpuHardware = null;
            ResetSensors();
            _hardware.Clear();
            _hardware.AddRange(_computer.Hardware);
            _gpuHardware = _hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia);

            try { NVAPI.Initialize(); } catch { /* Ignore NVAPI init fail */ }

            _needRefreshGpuHardware = true;
        }
    }

    public async Task UpdateAsync()
    {
        if (_isResetting)
        {
            return;
        }

        if (!IsLibreHardwareMonitorInitialized())
        {
            return;
        }

        await Task.Run(() =>
        {
            if (_isResetting)
            {
                return;
            }

            lock (_hardwareLock)
            {
                if (_isResetting)
                {
                    return;
                }

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
                catch (AccessViolationException){ }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Failed to update sensors: {ex.Message}");

                    if (ex is IndexOutOfRangeException)
                    {
                        Task.Run(ResetSensors);
                    }
                }
            }
        }).ConfigureAwait(false);
    }

    #region Helper

    private void ResetSensors()
    {
        _isResetting = true;

        try
        {
            lock (_hardwareLock)
            {
                try
                {
                    Log.Instance.Trace($"Starting sensor reset...");

                    _computer?.Close();

                    _hardware.Clear();
                    _cpuHardware = null;
                    _gpuHardware = null;
                    _amdGpuHardware = null;
                    _memoryHardware = null;

                    _computer?.Open();
                    _computer?.Accept(new UpdateVisitor());
                    _computer?.Reset();

                    if (_computer != null)
                    {
                        _hardware.AddRange(_computer.Hardware);
                        _cpuHardware = _hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
                        _amdGpuHardware = _hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuAmd && !Regex.IsMatch(h.Name, REGEX_AMD_GPU_INTEGRATED, RegexOptions.IgnoreCase));
                        _gpuHardware = _hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia);
                        _memoryHardware = _hardware.FirstOrDefault(h => h is { HardwareType: HardwareType.Memory, Name: SENSOR_NAME_TOTAL_MEMORY });
                    }

                    Log.Instance.Trace($"Sensors have been reset and hardware references refreshed.");
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Error resetting sensors: {ex}");
                }
            }
        }
        finally
        {
            _isResetting = false;
        }
    }

    private static string StripName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return UNKNOWN_NAME;
        }

        var cleanedName = name.Trim();

        if (cleanedName.Contains("AMD", StringComparison.OrdinalIgnoreCase))
        {
            cleanedName = Regex.Replace(cleanedName, REGEX_STRIP_AMD, "", RegexOptions.IgnoreCase);
        }
        else if (cleanedName.Contains("Intel", StringComparison.OrdinalIgnoreCase))
        {
            cleanedName = Regex.Replace(cleanedName, REGEX_STRIP_INTEL, "", RegexOptions.IgnoreCase);
        }
        else if (cleanedName.Contains("Nvidia", StringComparison.OrdinalIgnoreCase) || cleanedName.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(cleanedName, REGEX_STRIP_NVIDIA);
            if (match.Success)
            {
                cleanedName = match.Groups[1].Value;
            }
        }

        return Regex.Replace(cleanedName, REGEX_CLEAN_SPACES, " ").Trim();
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
        string? pawnIoPath = Registry.GetValue(REG_KEY_PAWN_IO, REG_VAL_INSTALL_LOC, null) as string;

        if (string.IsNullOrEmpty(pawnIoPath))
        {
            pawnIoPath = Registry.GetValue(REG_KEY_PAWN_IO_WOW64, REG_VAL_INSTALL_DIR, null) as string;
        }

        if (string.IsNullOrEmpty(pawnIoPath))
        {
            pawnIoPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), FOLDER_PAWN_IO);
        }

        return Directory.Exists(pawnIoPath);
    }
    #endregion

    public void Dispose()
    {
        lock (_hardwareLock)
        {
            _computer?.Close();
            _computer = null;
            _hardwareInitialized = false;
        }
        _initSemaphore.Dispose();
        GC.SuppressFinalize(this);
    }
}