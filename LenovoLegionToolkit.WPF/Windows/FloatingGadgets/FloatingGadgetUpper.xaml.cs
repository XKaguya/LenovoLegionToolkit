using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public partial class FloatingGadgetUpper
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly SensorsController _controller = IoCContainer.Resolve<SensorsController>();
    private readonly SensorsGroupController _sensorsGroupControllers = IoCContainer.Resolve<SensorsGroupController>();

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly StringBuilder _stringBuilder = new(64);
    private DateTime _lastUpdate = DateTime.MinValue;

    private CancellationTokenSource? _cts;
    private bool _positionSet;

    public FloatingGadgetUpper()
    {
        InitializeComponent();

        IsVisibleChanged += FloatingGadget_IsVisibleChanged;
        SourceInitialized += OnSourceInitialized!;
        Closed += FloatingGadget_Closed!;
        Loaded += OnLoaded;
        ContentRendered += OnContentRendered;

        var mi = Compatibility.GetMachineInformationAsync().Result;
        if (mi.Properties.IsAmdDevice)
        {
            _pchName.Text = Resource.SensorsControl_Motherboard_Title;
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private void OnSourceInitialized(object sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(SetWindowPosition), DispatcherPriority.Loaded);
    }

    private void OnContentRendered(object sender, EventArgs e)
    {
        if (!_positionSet)
        {
            Dispatcher.BeginInvoke(new Action(SetWindowPosition), DispatcherPriority.Render);
        }
    }

    private void SetWindowPosition()
    {
        if (double.IsNaN(ActualWidth) || ActualWidth <= 0)
            return;

        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        var workArea = SystemParameters.WorkArea;

        var left = (screenWidth - ActualWidth) / 2;
        var top = workArea.Top;

        Left = left;
        Top = top;
        _positionSet = true;
    }

    private async void FloatingGadget_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _cts?.Cancel();
            _cts?.Dispose();

            _cts = new CancellationTokenSource();
            await TheRing(_cts.Token);
        }
        else
        {
            _cts?.Cancel();
        }
    }

    private void FloatingGadget_Closed(object sender, EventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _refreshLock.Dispose();
    }

    public void UpdateSensorData(
        double cpuUsage, double cpuFrequency, double cpuTemp, double cpuPower,
        double gpuUsage, double gpuFrequency, double gpuTemp, double gpuVramTemp, double gpuPower,
        double memUsage, double pchTemp, double memTemp,
        int cpuFanSpeed, int gpuFanSpeed, int pchFanSpeed)
    {
        if ((DateTime.Now - _lastUpdate).TotalMilliseconds < 100) return;
        _lastUpdate = DateTime.Now;

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0}MHz", cpuFrequency);
        _cpuFrequency.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}°C", cpuTemp);
        _cpuTemperature.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F1}W", cpuPower);
        _cpuPower.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0}MHz", gpuFrequency);
        _gpuFrequency.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}°C", gpuTemp);
        _gpuTemperature.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}°C", gpuVramTemp);
        _gpuVramTemperature.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F1}W", gpuPower);
        _gpuPower.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}%", memUsage);
        _memUsage.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}°C", memTemp);
        _memTemperature.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:F0}°C", pchTemp);
        _pchTemperature.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0}RPM", cpuFanSpeed);
        _cpuFanSpeed.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0}RPM", gpuFanSpeed);
        _gpuFanSpeed.Text = _stringBuilder.ToString();

        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0}RPM", pchFanSpeed);
        _pchFanSpeed.Text = _stringBuilder.ToString();
    }

    public async Task TheRing(CancellationToken token)
    {
        if (!await _refreshLock.WaitAsync(0, token))
            return;

        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await RefreshDataAsync(token);
                    await Task.Delay(TimeSpan.FromSeconds(_settings.Store.FloatingGadgetsRefreshInterval), token);
                }
                catch (Exception) { }
            }
        }
        finally
        {
            try
            {
                _refreshLock.Release();
            }
            catch (ObjectDisposedException) { }
        }
    }

    private async Task RefreshDataAsync(CancellationToken token)
    {
        await _sensorsGroupControllers.UpdateAsync();

        var dataTask = _controller.GetDataAsync();
        var cpuPowerTask = _sensorsGroupControllers.GetCpuPowerAsync();
        var gpuPowerTask = _sensorsGroupControllers.GetGpuPowerAsync();
        var gpuVramTask = _sensorsGroupControllers.GetGpuVramTemperatureAsync();
        var memoryUsageTask = _sensorsGroupControllers.GetMemoryUsageAsync();
        var memoryTemperaturesTask = _sensorsGroupControllers.GetHighestMemoryTemperatureAsync();

        await Task.WhenAll(dataTask, cpuPowerTask, gpuPowerTask, gpuVramTask, memoryUsageTask, memoryTemperaturesTask);

        var data = dataTask.Result;
        var cpuPower = cpuPowerTask.Result;
        var gpuPower = gpuPowerTask.Result;

        await Dispatcher.BeginInvoke(() => UpdateSensorData(
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
            data.PCH.Temperature,
            memoryTemperaturesTask.Result,
            data.CPU.FanSpeed,
            data.GPU.FanSpeed,
            data.PCH.FanSpeed
        ), DispatcherPriority.Normal);
    }
}