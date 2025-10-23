using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public partial class FloatingGadget
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly SensorsController _controller = IoCContainer.Resolve<SensorsController>();
    private readonly SensorsGroupController _sensorsGroupControllers = IoCContainer.Resolve<SensorsGroupController>();

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private Task? _refreshTask;

    private CancellationTokenSource? _cts = null;

    public FloatingGadget()
    {
        InitializeComponent();

        IsVisibleChanged += FloatingGadget_IsVisibleChanged;
        this.SourceInitialized += OnSourceInitialized;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private void OnSourceInitialized(object sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TOOLWINDOW);
    }

    private async void FloatingGadget_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _cts = new CancellationTokenSource();
            await TheRing(_cts);
        }
        else
        {
            if (_cts != null)
            {
                await _cts.CancelAsync();
            }
        }
    }

    public async void UpdateSensorData(
                double cpuUsage, double cpuFrequency, double cpuTemp, double cpuPower,
                double gpuUsage, double gpuFrequency, double gpuTemp, double gpuVramTemp, double gpuPower,
                double memUsage, double pchTemp, double memTemp, double disk0Temperature, double disk1Temperature,
                int cpuFanSpeed, int gpuFanSpeed, int pchFanSpeed)
    {
        _cpuUsage.Text = $"{cpuUsage:F0}%";
        _cpuFrequency.Text = $"{cpuFrequency}Mhz";
        _cpuTemperature.Text = $"{cpuTemp:F0}°C";
        _cpuPower.Text = $"{cpuPower:F1} W";

        _gpuUsage.Text = $"{gpuUsage:F0}%";
        _gpuFrequency.Text = $"{gpuFrequency}Mhz";
        _gpuTemperature.Text = $"{gpuTemp:F0}°C";
        _gpuVramTemperature.Text = $"{gpuVramTemp:F0}°C";
        _gpuPower.Text = $"{gpuPower:F1} W";

        _memUsage.Text = $"{memUsage:F0}%";
        _pchTemperature.Text = $"{pchTemp:F0}°C";
        _memTemperature.Text = $"{memTemp:F0}°C";
        _disk0Temperature.Text = $"{disk0Temperature:F0}°C";
        _disk1Temperature.Text = $"{disk1Temperature:F0}°C";

        _cpuFanSpeed.Text = $"{cpuFanSpeed} RPM";
        _gpuFanSpeed.Text = $"{gpuFanSpeed} RPM";
        _pchFanSpeed.Text = $"{pchFanSpeed} RPM";
    }

    public async Task TheRing(CancellationTokenSource cancellationTokenSource)
    {
        if (!await _refreshLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                _refreshTask = Task.Run(async () =>
                {
                    var dataTask = _controller.GetDataAsync();
                    var cpuPowerTask = _sensorsGroupControllers.GetCpuPowerAsync();
                    var gpuPowerTask = _sensorsGroupControllers.GetGpuPowerAsync();
                    var gpuVramTask = _sensorsGroupControllers.GetGpuVramTemperatureAsync();
                    var diskTemperaturesTask = _sensorsGroupControllers.GetSSDTemperaturesAsync();
                    var memoryUsageTask = _sensorsGroupControllers.GetMemoryUsageAsync();
                    var memoryTemperaturesTask = _sensorsGroupControllers.GetHighestMemoryTemperatureAsync();

                    await Task.WhenAll(dataTask, cpuPowerTask, gpuPowerTask, gpuVramTask, diskTemperaturesTask, memoryUsageTask, memoryTemperaturesTask);

                    var data = dataTask.Result;
                    var cpuPower = cpuPowerTask.Result;
                    var gpuPower = gpuPowerTask.Result;

                    await Application.Current.Dispatcher.InvokeAsync(() => UpdateSensorData(
                            data.CPU.Utilization,
                            data.CPU.CoreClock,
                            data.CPU.Temperature,
                            cpuPower,
                            data.GPU.Utilization,
                            data.GPU.CoreClock,
                            data.GPU.Temperature,
                            gpuVramTask.Result,
                            gpuPower,
                            memoryUsageTask.Result,
                            memoryTemperaturesTask.Result,
                            data.PCH.Temperature,
                            diskTemperaturesTask.Result.Item1,
                            diskTemperaturesTask.Result.Item2,
                            data.CPU.FanSpeed,
                            data.GPU.FanSpeed,
                            data.PCH.FanSpeed
                        ), DispatcherPriority.Background);
                });

                await _refreshTask;
                await Task.Delay(TimeSpan.FromSeconds(_settings.Store.FloatingGadgetsRefreshInterval), cancellationTokenSource.Token);
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}