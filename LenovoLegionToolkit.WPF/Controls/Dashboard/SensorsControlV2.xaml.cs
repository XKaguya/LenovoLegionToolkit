using Humanizer;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Wpf.Ui.Common;
using MenuItem = Wpf.Ui.Controls.MenuItem;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public partial class SensorsControlV2
{
    private readonly SensorsController _controller = IoCContainer.Resolve<SensorsController>();
    private readonly SensorsGroupController _sensorsGroupControllers = IoCContainer.Resolve<SensorsGroupController>();
    private readonly SensorsControlSettings _sensorsControlSettings = IoCContainer.Resolve<SensorsControlSettings>();
    private readonly ApplicationSettings _applicationSettings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly DashboardSettings _dashboardSettings = IoCContainer.Resolve<DashboardSettings>();
    private CancellationTokenSource? _cts;
    private Task? _refreshTask;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _updateLock = new();
    private readonly Task<string> _cpuNameTask;
    private Task<string>? _gpuNameTask;
    private HashSet<SensorItem> _activeSensorItems = new();
    private Dictionary<SensorItem, FrameworkElement> _sensorItemToControlMap;

    public SensorsControlV2()
    {
        InitializeComponent();
        InitializeContextMenu();
        IsVisibleChanged += SensorsControl_IsVisibleChanged;
        _cpuNameTask = GetProcessedCpuName();
        _sensorItemToControlMap = new Dictionary<SensorItem, FrameworkElement>
        {
            { SensorItem.CpuUtilization, _cpuUtilizationGrid },
            { SensorItem.CpuFrequency, _cpuCoreClockGrid },
            { SensorItem.CpuFanSpeed, _cpuFanSpeedGrid },
            { SensorItem.CpuTemperature, _cpuTemperatureGrid },
            { SensorItem.CpuPower, _cpuPowerGrid },
            { SensorItem.GpuUtilization, _gpuUtilizationGrid },
            { SensorItem.GpuFrequency, _gpuCoreClockGrid },
            { SensorItem.GpuFanSpeed, _gpuFanSpeedGrid },
            { SensorItem.GpuTemperatures, _gpuTemperatureGrid },
            { SensorItem.GpuPower, _gpuPowerGrid },
            { SensorItem.PchFanSpeed, _pchFanSpeedGrid },
            { SensorItem.PchTemperature, _pchTemperatureGrid },
            { SensorItem.BatteryState, _batteryStateGrid },
            { SensorItem.BatteryLevel, _batteryLevelGrid },
            { SensorItem.MemoryUtilization, _memoryUtilizationGrid },
            { SensorItem.MemoryTemperature, _memoryTemperatureGrid },
            { SensorItem.Disk1Temperature, _disk1TemperatureGrid },
            { SensorItem.Disk2Temperature, _disk2TemperatureGrid }
        };

        var mi = Compatibility.GetMachineInformationAsync().Result;
        if (mi.Properties.IsAmdDevice)
        {
            _pchGridName.Text = Resource.SensorsControl_Motherboard_Temperature;
        }
    }

    private void InitializeContextMenu()
    {
        ContextMenu = new ContextMenu();
        ContextMenu.Items.Add(new MenuItem { Header = Resource.SensorsControl_RefreshInterval, IsEnabled = false });
        foreach (var interval in new[] { 1, 2, 3, 5 })
        {
            var item = new MenuItem
            {
                SymbolIcon = _dashboardSettings.Store.SensorsRefreshIntervalSeconds == interval
                    ? SymbolRegular.Checkmark24
                    : SymbolRegular.Empty,
                Header = TimeSpan.FromSeconds(interval).Humanize(culture: Resource.Culture)
            };
            item.Click += (_, _) =>
            {
                _dashboardSettings.Store.SensorsRefreshIntervalSeconds = interval;
                _dashboardSettings.SynchronizeStore();
                InitializeContextMenu();
            };
            ContextMenu.Items.Add(item);
        }
    }

    private async void SensorsControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _activeSensorItems.Clear();
            foreach (SensorItem item in _sensorsControlSettings.Store.VisibleItems!)
            {
                _activeSensorItems.Add(item);
            }
            await StartRefreshLoop();
        }
        else
        {
            await StopRefreshLoop();
        }
    }

    private async Task StartRefreshLoop()
    {
        if (!await _refreshLock.WaitAsync(0)) return;
        try
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            if (!await _controller.IsSupportedAsync().ConfigureAwait(false))
            {
                Dispatcher.Invoke(() => Visibility = Visibility.Collapsed);
                return;
            }
            await _controller.PrepareAsync().ConfigureAwait(false);
            _refreshTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await _sensorsGroupControllers.UpdateAsync();

                        _gpuNameTask = GetProcessedGpuName();
                        var dataTask = _controller.GetDataAsync();
                        var cpuPowerTask = _sensorsGroupControllers.GetCpuPowerAsync();
                        var gpuPowerTask = _sensorsGroupControllers.GetGpuPowerAsync();
                        var gpuVramTask = _sensorsGroupControllers.GetGpuVramTemperatureAsync();
                        var diskTemperaturesTask = _sensorsGroupControllers.GetSSDTemperaturesAsync();
                        var memoryUsageTask = _sensorsGroupControllers.GetMemoryUsageAsync();
                        var memoryTemperaturesTask = _sensorsGroupControllers.GetHighestMemoryTemperatureAsync();
                        var batteryInfoTask = Task.Run(() => Battery.GetBatteryInformation());
                        await Task.WhenAll(
                            dataTask,
                            cpuPowerTask,
                            gpuPowerTask,
                            gpuVramTask,
                            diskTemperaturesTask,
                            memoryUsageTask,
                            memoryTemperaturesTask,
                            batteryInfoTask
                        ).ConfigureAwait(false);
                        await Dispatcher.BeginInvoke(() => UpdateAllSensorValues(dataTask.Result, cpuPowerTask.Result, gpuPowerTask.Result, gpuVramTask.Result, diskTemperaturesTask.Result, memoryUsageTask.Result, memoryTemperaturesTask.Result, batteryInfoTask.Result), DispatcherPriority.Background);
                        await Task.Delay(TimeSpan.FromSeconds(_dashboardSettings.Store.SensorsRefreshIntervalSeconds), token);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        Log.Instance.Trace($"Sensor refresh failed: {ex}");
                        await Dispatcher.BeginInvoke(ClearAllSensorValues);
                    }
                }
            }, token);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task StopRefreshLoop()
    {
        if (_cts is not null)
            await _cts.CancelAsync();
        _cts = null;
        if (_refreshTask is not null)
            await _refreshTask;
        _refreshTask = null;
    }

    private void ClearAllSensorValues()
    {
        lock (_updateLock)
        {
            UpdateValue(_cpuUtilizationBar, _cpuUtilizationLabel, -1, -1, "-");
            UpdateValue(_cpuCoreClockBar, _cpuCoreClockLabel, -1, -1, "-");
            UpdateValue(_cpuTemperatureBar, _cpuTemperatureLabel, -1, -1, "-");
            UpdateValue(_cpuFanSpeedBar, _cpuFanSpeedLabel, -1, -1, "-");
            UpdateValue(_cpuPowerLabel, "-");
            UpdateValue(_gpuUtilizationBar, _gpuUtilizationLabel, -1, -1, "-");
            UpdateValue(_gpuCoreClockBar, _gpuCoreClockLabel, -1, -1, "-");
            UpdateValue(_gpuFanSpeedBar, _gpuFanSpeedLabel, -1, -1, "-");
            UpdateValue(_gpuCoreTemperatureLabel, "-");
            UpdateValue(_gpuMemoryTemperatureLabel, "-");
            UpdateValue(_gpuPowerLabel, "-");
            UpdateValue(_pchTemperatureBar, _pchTemperatureLabel, -1, -1, "-");
            UpdateValue(_pchFanSpeedBar, _pchFanSpeedLabel, -1, -1, "-");
            UpdateValue(_disk1TemperatureBar, _disk1TemperatureLabel, -1, -1, "-");
            UpdateValue(_disk2TemperatureBar, _disk2TemperatureLabel, -1, -1, "-");
            UpdateValue(_memoryUtilizationBar, _memoryUtilizationLabel, -1, -1, "-");
            UpdateValue(_memoryTemperatureBar, _memoryTemperatureLabel, -1, -1, "-");
            UpdateBatteryStatus(_batteryStateLabel, null);
            UpdateValue(_batteryLevelBar, _batteryLevelLabel, -1, -1, "-");
        }
    }

    private void UpdateAllSensorValues(SensorsData data, float cpuPower, float gpuPower, float gpuVramTemp, (float, float) diskTemps, float memoryUsage, double memoryTemp, BatteryInformation? batteryInfo)
    {
        lock (_updateLock)
        {
            foreach (var kv in _sensorItemToControlMap)
            {
                var control = kv.Value;
                control.Visibility = _activeSensorItems.Contains(kv.Key) ? Visibility.Visible : Visibility.Collapsed;
            }

            _cpuCardName.Text = _cpuNameTask.Result;
            _gpuCardName.Text = _gpuNameTask!.Result;

            if (_activeSensorItems.Contains(SensorItem.CpuUtilization)) UpdateValue(_cpuUtilizationBar, _cpuUtilizationLabel, data.CPU.MaxUtilization, data.CPU.Utilization, $"{data.CPU.Utilization}%");
            if (_activeSensorItems.Contains(SensorItem.CpuFrequency)) UpdateValue(_cpuCoreClockBar, _cpuCoreClockLabel, data.CPU.MaxCoreClock, data.CPU.CoreClock, $"{data.CPU.CoreClock / 1000.0:0.0} {Resource.GHz}", $"{data.CPU.MaxCoreClock / 1000.0:0.0} {Resource.GHz}");
            if (_activeSensorItems.Contains(SensorItem.CpuTemperature)) UpdateValue(_cpuTemperatureBar, _cpuTemperatureLabel, data.CPU.MaxTemperature, data.CPU.Temperature, GetTemperatureText(data.CPU.Temperature), GetTemperatureText(data.CPU.MaxTemperature));
            if (_activeSensorItems.Contains(SensorItem.CpuFanSpeed)) UpdateValue(_cpuFanSpeedBar, _cpuFanSpeedLabel, data.CPU.MaxFanSpeed, data.CPU.FanSpeed, $"{data.CPU.FanSpeed} {Resource.RPM}", $"{data.CPU.MaxFanSpeed} {Resource.RPM}");
            if (_activeSensorItems.Contains(SensorItem.CpuPower)) UpdateValue(_cpuPowerLabel, $"{cpuPower:0}W");
            if (_activeSensorItems.Contains(SensorItem.GpuUtilization)) UpdateValue(_gpuUtilizationBar, _gpuUtilizationLabel, data.GPU.MaxUtilization, data.GPU.Utilization, $"{data.GPU.Utilization}%");
            if (_activeSensorItems.Contains(SensorItem.GpuFrequency)) UpdateValue(_gpuCoreClockBar, _gpuCoreClockLabel, data.GPU.MaxCoreClock, data.GPU.CoreClock, $"{data.GPU.CoreClock} {Resource.MHz}", $"{data.GPU.MaxCoreClock} {Resource.MHz}");
            if (_activeSensorItems.Contains(SensorItem.GpuCoreTemperature)) UpdateValue(_gpuCoreTemperatureLabel, data.GPU.MaxTemperature, data.GPU.Temperature, GetTemperatureText(data.GPU.Temperature), GetTemperatureText(data.GPU.MaxTemperature));
            if (_activeSensorItems.Contains(SensorItem.GpuVramTemperature)) UpdateValue(_gpuMemoryTemperatureLabel, data.GPU.MaxTemperature, data.GPU.Temperature, GetTemperatureText(gpuVramTemp), GetTemperatureText(data.GPU.MaxTemperature));
            if (_activeSensorItems.Contains(SensorItem.GpuFanSpeed)) UpdateValue(_gpuFanSpeedBar, _gpuFanSpeedLabel, data.GPU.MaxFanSpeed, data.GPU.FanSpeed, $"{data.GPU.FanSpeed} {Resource.RPM}", $"{data.GPU.MaxFanSpeed} {Resource.RPM}");
            if (_activeSensorItems.Contains(SensorItem.GpuPower)) UpdateValue(_gpuPowerLabel, $"{gpuPower:0}W");
            if (_activeSensorItems.Contains(SensorItem.PchTemperature)) UpdateValue(_pchTemperatureBar, _pchTemperatureLabel, data.PCH.MaxTemperature, data.PCH.Temperature, GetTemperatureText(data.PCH.Temperature), GetTemperatureText(data.PCH.MaxTemperature));
            if (_activeSensorItems.Contains(SensorItem.PchFanSpeed)) UpdateValue(_pchFanSpeedBar, _pchFanSpeedLabel, data.PCH.MaxFanSpeed, data.PCH.FanSpeed, $"{data.PCH.FanSpeed} {Resource.RPM}", $"{data.PCH.MaxFanSpeed} {Resource.RPM}");
            if (_activeSensorItems.Contains(SensorItem.Disk1Temperature)) UpdateValue(_disk1TemperatureBar, _disk1TemperatureLabel, 100, diskTemps.Item1, GetTemperatureText(diskTemps.Item1), GetTemperatureText(100));
            if (_activeSensorItems.Contains(SensorItem.Disk2Temperature)) UpdateValue(_disk2TemperatureBar, _disk2TemperatureLabel, 100, diskTemps.Item2, GetTemperatureText(diskTemps.Item2), GetTemperatureText(100));
            if (_activeSensorItems.Contains(SensorItem.MemoryUtilization)) UpdateValue(_memoryUtilizationBar, _memoryUtilizationLabel, 100, memoryUsage, $"{memoryUsage:0}%", "100%");
            if (_activeSensorItems.Contains(SensorItem.MemoryTemperature)) UpdateValue(_memoryTemperatureBar, _memoryTemperatureLabel, 100, memoryTemp, GetTemperatureText(memoryTemp), GetTemperatureText(100));
            if (_activeSensorItems.Contains(SensorItem.BatteryState)) UpdateBatteryStatus(_batteryStateLabel, batteryInfo);
            if (_activeSensorItems.Contains(SensorItem.BatteryLevel)) UpdateValue(_batteryLevelBar, _batteryLevelLabel, 100, batteryInfo?.BatteryPercentage ?? 0, batteryInfo != null ? $"{batteryInfo.Value.BatteryPercentage}%" : "-", "100%");

            UpdateCardVisibility(_cpuCard, new[] { SensorItem.CpuUtilization, SensorItem.CpuFrequency, SensorItem.CpuFanSpeed, SensorItem.CpuTemperature, SensorItem.CpuPower });
            UpdateCardVisibility(_gpuCard, new[] { SensorItem.GpuUtilization, SensorItem.GpuFrequency, SensorItem.GpuFanSpeed, SensorItem.GpuTemperatures, SensorItem.GpuPower });
            UpdateMotherboardCardVisibility();
            UpdateMemoryDiskCardVisibility();
        }
    }

    private void UpdateCardVisibility(FrameworkElement card, IEnumerable<SensorItem> sensorItems)
    {
        bool hasVisibleItems = sensorItems.Any(item => _activeSensorItems.Contains(item));
        card.Visibility = hasVisibleItems ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateMotherboardCardVisibility()
    {
        bool pchVisible = _activeSensorItems.Contains(SensorItem.PchFanSpeed) || _activeSensorItems.Contains(SensorItem.PchTemperature);
        bool batteryVisible = _activeSensorItems.Contains(SensorItem.BatteryState) || _activeSensorItems.Contains(SensorItem.BatteryLevel);

        _pchStackPanel.Visibility = pchVisible ? Visibility.Visible : Visibility.Collapsed;
        _batteryGrid.Visibility = batteryVisible ? Visibility.Visible : Visibility.Collapsed;

        _seperator1.Visibility = (pchVisible && batteryVisible) ? Visibility.Visible : Visibility.Collapsed;

        _motherboardCard.Visibility = (pchVisible || batteryVisible) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateMemoryDiskCardVisibility()
    {
        var memoryItems = new[] { SensorItem.MemoryUtilization, SensorItem.MemoryTemperature };
        var diskItems = new[] { SensorItem.Disk1Temperature, SensorItem.Disk2Temperature };

        bool memoryVisible = memoryItems.Any(item => _activeSensorItems.Contains(item));
        bool diskVisible = diskItems.Any(item => _activeSensorItems.Contains(item));

        _memoryGrid.Visibility = memoryVisible ? Visibility.Visible : Visibility.Collapsed;
        _diskGrid.Visibility = diskVisible ? Visibility.Visible : Visibility.Collapsed;

        _seperator2.Visibility = (memoryVisible && diskVisible) ? Visibility.Visible : Visibility.Collapsed;

        _memoryDiskCard.Visibility = (memoryVisible || diskVisible) ? Visibility.Visible : Visibility.Collapsed;
    }

    private Task<string> GetProcessedCpuName()
    {
        return _sensorsGroupControllers.GetCpuNameAsync();
    }

    private Task<string> GetProcessedGpuName()
    {
        return _sensorsGroupControllers.GetGpuNameAsync();
    }

    private string GetTemperatureText(double temperature)
    {
        if (temperature <= 0) return "-";
        if (_applicationSettings.Store.TemperatureUnit == TemperatureUnit.F)
        {
            temperature = temperature * 9 / 5 + 32;
            return $"{temperature:0}{Resource.Fahrenheit}";
        }
        return $"{temperature:0}{Resource.Celsius}";
    }

    private static void UpdateValue(RangeBase bar, TextBlock label, double max, double value, string text, string? toolTipText = null)
    {
        if (max < 0 || value < 0)
        {
            bar.Minimum = 0;
            bar.Maximum = 1;
            bar.Value = 0;
            label.Text = "-";
            label.ToolTip = null;
            label.Tag = 0;
        }
        else
        {
            bar.Minimum = 0;
            bar.Maximum = max;
            bar.Value = value;
            label.Text = text;
            label.ToolTip = toolTipText is null ? null : string.Format(Resource.SensorsControl_Maximum, toolTipText);
            label.Tag = value;
        }
    }

    private static void UpdateValue(TextBlock label, double max, double value, string text, string? toolTipText = null)
    {
        if (max < 0 || value < 0)
        {
            label.Text = "-";
            label.ToolTip = null;
            label.Tag = 0;
        }
        else
        {
            label.Text = text;
            label.ToolTip = toolTipText is null ? null : string.Format(Resource.SensorsControl_Maximum, toolTipText);
            label.Tag = value;
        }
    }

    private static void UpdateBatteryStatus(TextBlock label, BatteryInformation? batteryInfo)
    {
        if (batteryInfo is null)
        {
            label.Text = "-";
            label.ToolTip = null;
            label.Tag = null;
        }
        else
        {
            label.Text = batteryInfo.Value.IsCharging
                ? Resource.DashboardBattery_AcConnected
                : Resource.DashboardBattery_AcDisconnected;
        }
    }

    private static void UpdateValue(TextBlock label, string str)
    {
        var processedStr = str.Replace("W", "");
        if (int.TryParse(processedStr, out var result))
        {
            if (result <= 0)
            {
                label.Text = "-";
            }
            else
            {
                label.Text = str;
            }
        }
        else
        {
            label.Text = str;
        }
    }
}