// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) RAMSPDToolkit and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using Humanizer;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Settings;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Wpf.Ui.Common;
using MenuItem = Wpf.Ui.Controls.MenuItem;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public partial class SensorsControl
{
    private readonly SensorsController _controller = IoCContainer.Resolve<SensorsController>();
    private readonly SensorsGroupController _sensorsGroupControllers = IoCContainer.Resolve<SensorsGroupController>();
    private readonly ApplicationSettings _applicationSettings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly DashboardSettings _dashboardSettings = IoCContainer.Resolve<DashboardSettings>();

    private CancellationTokenSource? _cts;
    private Task? _refreshTask;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _updateLock = new();

    private static BatteryInformation? _cachedBatteryInfo;

    private readonly Task<string> _cpuNameTask;
    private readonly Task<string> _gpuNameTask;

    public SensorsControl()
    {
        InitializeComponent();
        InitializeContextMenu();

        IsVisibleChanged += SensorsControl_IsVisibleChanged;

        _cpuNameTask = GetProcessedCpuName();
        _gpuNameTask = GetProcessedGpuName();

        PreviewKeyDown += (s, e) => {
            if (e.Key == Key.System && e.SystemKey == Key.LeftAlt)
            {
                e.Handled = true;
                Keyboard.ClearFocus();
            }
        };
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
            await RefreshAsync();
            return;
        }

        if (_cts is not null)
            await _cts.CancelAsync();

        _cts = null;

        if (_refreshTask is not null)
            await _refreshTask;

        _refreshTask = null;
    }

    private async Task RefreshAsync()
    {
        if (!await _refreshLock.WaitAsync(0))
        {
            return;
        }

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
                        var dataTask = _controller.GetDataAsync();
                        var cpuPowerTask = _sensorsGroupControllers.GetCpuPowerAsync();
                        var gpuPowerTask = _sensorsGroupControllers.GetGpuPowerAsync();
                        var diskTemperaturesTask = _sensorsGroupControllers.GetSSDTemperaturesAsync();
                        var memoryUsageTask = _sensorsGroupControllers.GetMemoryUsageAsync();
                        var memoryTemperaturesTask = _sensorsGroupControllers.GetHighestMemoryTemperatureAsync();
                        var batteryInfoTask = Task.Run(() => Battery.GetBatteryInformation());

                        await Task.WhenAll(
                            dataTask,
                            cpuPowerTask,
                            gpuPowerTask,
                            diskTemperaturesTask,
                            memoryUsageTask,
                            memoryTemperaturesTask,
                            batteryInfoTask
                        ).ConfigureAwait(false);

                        await Dispatcher.InvokeAsync(() =>
                        {
                            lock (_updateLock)
                            {
                                var data = dataTask.Result;
                                var (diskTemp0, diskTemp1) = diskTemperaturesTask.Result;

                                UpdateValue(_cpuCardName, _cpuNameTask.Result);
                                UpdateValue(_gpuCardName, _gpuNameTask.Result);

                                UpdateValue(_cpuUtilizationBar, _cpuUtilizationLabel, data.CPU.MaxUtilization, data.CPU.Utilization, $"{data.CPU.Utilization}%");
                                UpdateValue(_cpuCoreClockBar, _cpuCoreClockLabel, data.CPU.MaxCoreClock, data.CPU.CoreClock, $"{data.CPU.CoreClock / 1000.0:0.0} {Resource.GHz}", $"{data.CPU.MaxCoreClock / 1000.0:0.0} {Resource.GHz}");
                                UpdateValue(_cpuTemperatureBar, _cpuTemperatureLabel, data.CPU.MaxTemperature, data.CPU.Temperature, GetTemperatureText(data.CPU.Temperature), GetTemperatureText(data.CPU.MaxTemperature));
                                UpdateValue(_cpuFanSpeedBar, _cpuFanSpeedLabel, data.CPU.MaxFanSpeed, data.CPU.FanSpeed, $"{data.CPU.FanSpeed} {Resource.RPM}", $"{data.CPU.MaxFanSpeed} {Resource.RPM}");
                                UpdateValue(_cpuPowerLabel, $"{cpuPowerTask.Result:0}W");

                                UpdateValue(_gpuUtilizationBar, _gpuUtilizationLabel, data.GPU.MaxUtilization, data.GPU.Utilization, $"{data.GPU.Utilization}%");
                                UpdateValue(_gpuCoreClockBar, _gpuCoreClockLabel, data.GPU.MaxCoreClock, data.GPU.CoreClock, $"{data.GPU.CoreClock} {Resource.MHz}", $"{data.GPU.MaxCoreClock} {Resource.MHz}");
                                UpdateValue(_gpuCoreTemperatureLabel, data.GPU.MaxTemperature, data.GPU.Temperature, GetTemperatureText(data.GPU.Temperature), GetTemperatureText(data.GPU.MaxTemperature));
                                UpdateValue(_gpuMemoryTemperatureLabel, data.GPU.MaxTemperature, data.GPU.Temperature, GetTemperatureText(data.GPU.Temperature), GetTemperatureText(data.GPU.MaxTemperature));
                                UpdateValue(_gpuFanSpeedBar, _gpuFanSpeedLabel, data.GPU.MaxFanSpeed, data.GPU.FanSpeed, $"{data.GPU.FanSpeed} {Resource.RPM}", $"{data.GPU.MaxFanSpeed} {Resource.RPM}");
                                UpdateValue(_gpuPowerLabel, $"{gpuPowerTask.Result:0}W");

                                UpdateValue(_pchTemperatureBar, _pchTemperatureLabel, data.PCH.MaxTemperature, data.PCH.Temperature, GetTemperatureText(data.PCH.Temperature), GetTemperatureText(data.PCH.MaxTemperature));
                                UpdateValue(_pchFanSpeedBar, _pchFanSpeedLabel, data.PCH.MaxFanSpeed, data.PCH.FanSpeed, $"{data.PCH.FanSpeed} {Resource.RPM}", $"{data.PCH.MaxFanSpeed} {Resource.RPM}");

                                UpdateValue(_disk0TemperatureBar, _disk0TemperatureLabel, 100, diskTemp0, GetTemperatureText(diskTemp0), GetTemperatureText(100));
                                UpdateValue(_disk1TemperatureBar, _disk1TemperatureLabel, 100, diskTemp1, GetTemperatureText(diskTemp1), GetTemperatureText(100));

                                UpdateValue(_memoryUtilizationBar, _memoryUtilizationLabel, 100, memoryUsageTask.Result, $"{memoryUsageTask.Result:0}%", "100%");
                                UpdateValue(_memoryTemperatureBar, _memoryTemperatureLabel, 100, memoryTemperaturesTask.Result, GetTemperatureText(memoryTemperaturesTask.Result), GetTemperatureText(100));

                                _cachedBatteryInfo = batteryInfoTask.Result;
                                UpdateBatteryStatus(_batteryStateLabel, _cachedBatteryInfo);
                                UpdateValue(_batteryLevelBar, _batteryLevelLabel, 100, _cachedBatteryInfo?.BatteryPercentage ?? 0, _cachedBatteryInfo != null ? $"{_cachedBatteryInfo.Value.BatteryPercentage}%" : "-", "100%");
                            }
                        }, DispatcherPriority.Render);

                        await Task.Delay(TimeSpan.FromSeconds(_dashboardSettings.Store.SensorsRefreshIntervalSeconds), token);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"Sensor refresh failed: {ex}");

                        await Dispatcher.InvokeAsync(() =>
                        {
                            lock (_updateLock)
                            {
                                UpdateValue(_cpuUtilizationBar, _cpuUtilizationLabel, -1, -1, "");
                                UpdateValue(_cpuCoreClockBar, _cpuCoreClockLabel, -1, -1, "");
                                UpdateValue(_cpuTemperatureBar, _cpuTemperatureLabel, -1, -1, "");
                                UpdateValue(_cpuFanSpeedBar, _cpuFanSpeedLabel, -1, -1, "");
                                UpdateValue(_cpuPowerLabel, "-");

                                UpdateValue(_gpuUtilizationBar, _gpuUtilizationLabel, -1, -1, "");
                                UpdateValue(_gpuCoreClockBar, _gpuCoreClockLabel, -1, -1, "");
                                UpdateValue(_gpuCoreTemperatureLabel, -1, -1, "");
                                UpdateValue(_gpuMemoryTemperatureLabel, -1, -1, "");
                                UpdateValue(_gpuFanSpeedBar, _gpuFanSpeedLabel, -1, -1, "");
                                UpdateValue(_gpuPowerLabel, "-");

                                UpdateValue(_pchTemperatureBar, _pchTemperatureLabel, -1, -1, "");
                                UpdateValue(_pchFanSpeedBar, _pchFanSpeedLabel, -1, -1, "");

                                UpdateValue(_disk0TemperatureBar, _disk0TemperatureLabel, -1, -1, "");
                                UpdateValue(_disk1TemperatureBar, _disk1TemperatureLabel, -1, -1, "");

                                UpdateValue(_memoryUtilizationBar, _memoryUtilizationLabel, -1, -1, "");
                                UpdateValue(_memoryTemperatureBar, _memoryTemperatureLabel, -1, -1, "");

                                UpdateBatteryStatus(_batteryStateLabel, null);
                                UpdateValue(_batteryLevelBar, _batteryLevelLabel, -1, -1, "-");
                            }
                        });
                    }
                }
            }, token);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<string> GetProcessedCpuName()
    {
        return await _sensorsGroupControllers.GetCpuNameAsync().ConfigureAwait(false);
    }

    private async Task<string> GetProcessedGpuName()
    {
        return await _sensorsGroupControllers.GetGpuNameAsync().ConfigureAwait(false);
    }

    private string GetTemperatureText(double temperature)
    {
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
        label.Text = str;
    }
}