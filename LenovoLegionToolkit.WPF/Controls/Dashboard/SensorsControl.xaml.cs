using Humanizer;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Settings;
using System;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Wpf.Ui.Common;
using MenuItem = Wpf.Ui.Controls.MenuItem;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public partial class SensorsControl
{
    private readonly SensorsController _controller = IoCContainer.Resolve<SensorsController>();
    private readonly ApplicationSettings _applicationSettings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly DashboardSettings _dashboardSettings = IoCContainer.Resolve<DashboardSettings>();

    private CancellationTokenSource? _cts;
    private Task? _refreshTask;

    private static double _cachedTotalMB = 0;
    private static double _lastMemoryUsagePercent = -1;

    public SensorsControl()
    {
        InitializeComponent();
        InitializeContextMenu();

        IsVisibleChanged += SensorsControl_IsVisibleChanged;

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
                SymbolIcon = _dashboardSettings.Store.SensorsRefreshIntervalSeconds == interval ? SymbolRegular.Checkmark24 : SymbolRegular.Empty,
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
            Refresh();
            return;
        }

        if (_cts is not null)
            await _cts.CancelAsync();

        _cts = null;

        if (_refreshTask is not null)
            await _refreshTask;

        _refreshTask = null;

        UpdateValues(SensorsData.Empty);
    }

    private void Refresh()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        var token = _cts.Token;

        _refreshTask = Task.Run(async () =>
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Sensors refresh started...");

            if (!await _controller.IsSupportedAsync())
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Sensors not supported.");

                Dispatcher.Invoke(() => Visibility = Visibility.Collapsed);
                return;
            }

            await _controller.PrepareAsync();

            if (_cachedTotalMB == 0)
            {
                await InitializeTotalMemoryCacheAsync();
            }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var data = await _controller.GetDataAsync();
                    var memoryUsageTask = UpdateMemoryUsageAsync();
                    Dispatcher.Invoke(() => UpdateValues(data));
                    await memoryUsageTask;
                    await Task.Delay(TimeSpan.FromSeconds(_dashboardSettings.Store.SensorsRefreshIntervalSeconds), token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Sensors refresh failed.", ex);

                    Dispatcher.Invoke(() => UpdateValues(SensorsData.Empty));
                }
            }

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Sensors refresh stopped.");
        }, token);
    }

    private async Task InitializeTotalMemoryCacheAsync()
    {
        await Task.Run(() =>
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            var totalBytes = searcher.Get().Cast<ManagementObject>()
                .Select(mo => Convert.ToUInt64(mo["TotalPhysicalMemory"]))
                .FirstOrDefault();

            if (totalBytes == 0)
                throw new InvalidOperationException("Failed to get total memory");

            _cachedTotalMB = totalBytes / (1024.0 * 1024);
        });
    }

    private async Task UpdateMemoryUsageAsync()
    {
        try
        {
            double availableMB = 0;
            await Task.Run(() =>
            {
                using var memClass = new ManagementClass("Win32_PerfFormattedData_PerfOS_Memory");
                using var instances = memClass.GetInstances();
                availableMB = instances.Cast<ManagementObject>()
                    .Select(mo => mo.Properties["AvailableMBytes"]?.Value)
                    .Where(val => val != null)
                    .Sum(val => Convert.ToDouble(val));
            });

            double usedMB = _cachedTotalMB - availableMB;
            if (usedMB < 0) usedMB = 0;

            _lastMemoryUsagePercent = (usedMB / _cachedTotalMB) * 100;

            Dispatcher.Invoke(() =>
            {
                UpdateValue(_memoryUtilizationBar, _memoryUtilizationLabel, 100, _lastMemoryUsagePercent,
                    $"{_lastMemoryUsagePercent:0}%", "100%");
            });
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Memory usage update failed", ex);
        }
    }

    private void UpdateValues(SensorsData data)
    {
        // CPU
        UpdateValue(_cpuUtilizationBar, _cpuUtilizationLabel, data.CPU.MaxUtilization, data.CPU.Utilization,
            $"{data.CPU.Utilization}%");
        UpdateValue(_cpuCoreClockBar, _cpuCoreClockLabel, data.CPU.MaxCoreClock, data.CPU.CoreClock,
            $"{data.CPU.CoreClock / 1000.0:0.0} {Resource.GHz}", $"{data.CPU.MaxCoreClock / 1000.0:0.0} {Resource.GHz}");
        UpdateValue(_cpuTemperatureBar, _cpuTemperatureLabel, data.CPU.MaxTemperature, data.CPU.Temperature,
            GetTemperatureText(data.CPU.Temperature), GetTemperatureText(data.CPU.MaxTemperature));
        UpdateValue(_cpuFanSpeedBar, _cpuFanSpeedLabel, data.CPU.MaxFanSpeed, data.CPU.FanSpeed,
            $"{data.CPU.FanSpeed} {Resource.RPM}", $"{data.CPU.MaxFanSpeed} {Resource.RPM}");

        // GPU
        UpdateValue(_gpuUtilizationBar, _gpuUtilizationLabel, data.GPU.MaxUtilization, data.GPU.Utilization,
            $"{data.GPU.Utilization}%");
        UpdateValue(_gpuCoreClockBar, _gpuCoreClockLabel, data.GPU.MaxCoreClock, data.GPU.CoreClock,
            $"{data.GPU.CoreClock} {Resource.MHz}", $"{data.GPU.MaxCoreClock} {Resource.MHz}");
        UpdateValue(_gpuCoreTemperatureLabel, data.GPU.MaxTemperature, data.GPU.Temperature,
            GetTemperatureText(data.GPU.Temperature), GetTemperatureText(data.GPU.MaxTemperature));
        UpdateValue(_gpuMemoryTemperatureLabel, data.GPU.MaxTemperature, data.GPU.Temperature,
            GetTemperatureText(data.GPU.Temperature), GetTemperatureText(data.GPU.MaxTemperature));
        UpdateValue(_gpuFanSpeedBar, _gpuFanSpeedLabel, data.GPU.MaxFanSpeed, data.GPU.FanSpeed,
            $"{data.GPU.FanSpeed} {Resource.RPM}", $"{data.GPU.MaxFanSpeed} {Resource.RPM}");

        // PCH
        UpdateValue(_pchTemperatureBar, _pchTemperatureLabel, data.PCH.MaxTemperature, data.PCH.Temperature,
            GetTemperatureText(data.PCH.Temperature), GetTemperatureText(data.PCH.MaxTemperature));
        UpdateValue(_pchFanSpeedBar, _pchFanSpeedLabel, data.PCH.MaxFanSpeed, data.PCH.FanSpeed,
            $"{data.PCH.FanSpeed} {Resource.RPM}", $"{data.PCH.MaxFanSpeed} {Resource.RPM}");

        // Memory
        if (_lastMemoryUsagePercent >= 0)
        {
            UpdateValue(_memoryUtilizationBar, _memoryUtilizationLabel, 100, _lastMemoryUsagePercent,
                $"{_lastMemoryUsagePercent:0}%", "100%");
        }
        else
        {
            UpdateValue(_memoryUtilizationBar, _memoryUtilizationLabel, 100, 0,
                "-", "100%");
        }

        // Battery
        var batteryData = Battery.GetBatteryInformation();

        UpdateBatteryStatus(_batteryStateLabel, batteryData);

        UpdateValue(_batteryLevelBar, _batteryLevelLabel, 100, batteryData.BatteryPercentage,
            $"{batteryData.BatteryPercentage}%", "100%");
    }

    private string GetTemperatureText(double temperature)
    {
        if (_applicationSettings.Store.TemperatureUnit == TemperatureUnit.F)
        {
            temperature *= 9.0 / 5.0;
            temperature += 32;
            return $"{temperature:0} {Resource.Fahrenheit}";
        }

        return $"{temperature:0} {Resource.Celsius}";
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

    private static void UpdateBatteryStatus(TextBlock label, BatteryInformation batteryInfo)
    {
        if (batteryInfo.IsCharging)
        {
            if (batteryInfo.DischargeRate > 0)
                label.Text = Resource.BatteryPage_ACAdapterConnectedAndCharging;

            label.Text = Resource.BatteryPage_ACAdapterConnectedNotCharging;
        }
        else
        {
            label.Text = Resource.BatteryPage_ACAdapterNotConnected;
        }
    }
}