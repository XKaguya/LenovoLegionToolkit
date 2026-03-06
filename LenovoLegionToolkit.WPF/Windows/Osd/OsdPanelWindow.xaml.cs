using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.WPF.Resources;
using System.Windows.Media;

namespace LenovoLegionToolkit.WPF.Windows.Osd;

public partial class OsdPanelWindow : OsdWindowBase
{
    private Style? _originalLabelStyle;
    private Style? _originalValueStyle;

    public OsdPanelWindow()
    {
        InitializeComponent();

        _itemsMap = new()
        {
            { OsdItem.Fps, _fps },
            { OsdItem.LowFps, _lowFps },
            { OsdItem.FrameTime, _frameTime },
            { OsdItem.CpuUtilization, _cpuUsage },
            { OsdItem.CpuFrequency, _cpuFrequency },
            { OsdItem.CpuPCoreFrequency, _cpuPFrequency },
            { OsdItem.CpuECoreFrequency, _cpuEFrequency },
            { OsdItem.CpuTemperature, _cpuTemperature },
            { OsdItem.CpuPower, _cpuPower },
            { OsdItem.CpuFan, _cpuFanSpeed },
            { OsdItem.GpuUtilization, _gpuUsage },
            { OsdItem.GpuFrequency, _gpuFrequency },
            { OsdItem.GpuTemperature, _gpuTemperature },
            { OsdItem.GpuVramTemperature, _gpuVramTemperature },
            { OsdItem.GpuPower, _gpuPower },
            { OsdItem.GpuFan, _gpuFanSpeed },
            { OsdItem.MemoryUtilization, _memUsage },
            { OsdItem.MemoryTemperature, _memTemperature },
            { OsdItem.PchTemperature, _pchTemperature },
            { OsdItem.PchFan, _pchFanSpeed },
            { OsdItem.Disk1Temperature, _disk0Temperature },
            { OsdItem.Disk2Temperature, _disk1Temperature },
        };

        _measurementGroups = new()
        {
            { _fpsGroup, ([OsdItem.Fps, OsdItem.LowFps, OsdItem.FrameTime], _separatorFps) },
            { _cpuGroup, ([OsdItem.CpuUtilization, OsdItem.CpuFrequency, OsdItem.CpuPCoreFrequency, OsdItem.CpuECoreFrequency, OsdItem.CpuTemperature, OsdItem.CpuPower, OsdItem.CpuFan], null) },
            { _gpuGroup, ([OsdItem.GpuUtilization, OsdItem.GpuFrequency, OsdItem.GpuTemperature, OsdItem.GpuVramTemperature, OsdItem.GpuPower, OsdItem.GpuFan], null) },
            { _memoryGroup, ([OsdItem.MemoryUtilization, OsdItem.MemoryTemperature], null) },
            { _pchGroup, ([OsdItem.PchTemperature, OsdItem.PchFan, OsdItem.Disk1Temperature, OsdItem.Disk2Temperature], null) }
        };

        if (_sensorsPanel.Resources["SensorLabelStyle"] is Style labelStyle)
            _originalLabelStyle = labelStyle;
        if (_sensorsPanel.Resources["SensorValueStyle"] is Style valueStyle)
            _originalValueStyle = valueStyle;

        InitOsd();
    }

    protected override double? SavedPositionX
    {
        get => _OsdSettings.Store.PanelPositionX;
        set => _OsdSettings.Store.PanelPositionX = value;
    }

    protected override double? SavedPositionY
    {
        get => _OsdSettings.Store.PanelPositionY;
        set => _OsdSettings.Store.PanelPositionY = value;
    }

    protected override void OnAmdDeviceDetected()
    {
        _pchName.Text = Resource.SensorsControl_Motherboard_Title;
    }

    protected override void ApplyAppearanceSettings()
    {
        base.ApplyAppearanceSettings();

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(_OsdSettings.Store.BackgroundColor);
            var alpha = (byte)(_OsdSettings.Store.BackgroundOpacity * 255);
            color.A = alpha;
            _rootBorder.Background = new SolidColorBrush(color);
        }
        catch
        {
            _rootBorder.Background = new SolidColorBrush(Color.FromArgb(153, 30, 30, 30));
        }

        double fontSize = _OsdSettings.Store.FontSize;

        if (_originalLabelStyle != null)
        {
            var newStyle = new Style(typeof(TextBlock), _originalLabelStyle);
            newStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, fontSize - 1));
            if (_labelBrush != null)
                newStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, _labelBrush));
            _sensorsPanel.Resources["SensorLabelStyle"] = newStyle;
        }

        if (_originalValueStyle != null)
        {
            var newStyle = new Style(typeof(TextBlock), _originalValueStyle);
            newStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, fontSize + 1));
            _sensorsPanel.Resources["SensorValueStyle"] = newStyle;
        }

        ApplyCornerRadius(_rootBorder);
    }

    protected override void SetDefaultWindowPosition()
    {
        if (double.IsNaN(ActualWidth) || ActualWidth <= 0) return;

        var workArea = SystemParameters.WorkArea;

        Left = workArea.Left;
        Top = workArea.Top + (workArea.Height - ActualHeight) / 2;
        _positionSet = true;
    }

    protected override void OnItemVisibilityChanged(FrameworkElement element, bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        if (element.Parent is not Panel panel) return;

        foreach (var child in System.Linq.Enumerable.OfType<TextBlock>(panel.Children))
        {
            if (child != element) child.Visibility = visibility;
        }
    }

    protected override void UpdateFpsDisplay(FpsDisplayData data)
    {
        if (data.FpsText != null)
        {
            SetTextIfChanged(_fps, data.FpsText);
            if (data.FpsBrush != null) SetForegroundIfChanged(_fps, data.FpsBrush);
        }

        if (data.LowFpsText != null)
        {
            SetTextIfChanged(_lowFps, data.LowFpsText);
            if (data.LowFpsBrush != null) SetForegroundIfChanged(_lowFps, data.LowFpsBrush);
        }

        if (data.FrameTimeText == null) return;

        SetTextIfChanged(_frameTime, data.FrameTimeText);
        if (data.FrameTimeBrush != null) SetForegroundIfChanged(_frameTime, data.FrameTimeBrush);
    }

    protected override void UpdateSensorData(SensorSnapshot data)
    {
        var store = _OsdSettings.Store;

        UpdateTextBlock(_cpuFrequency, data.CpuFrequency, "{0} MHz");
        UpdateTextBlock(_cpuPFrequency, data.CpuPClock, "{0:F0} MHz");
        UpdateTextBlock(_cpuEFrequency, data.CpuEClock, "{0:F0} MHz");
        UpdateTextBlock(_cpuUsage, data.CpuUsage, "{0:F0}%", store.UsageThresholdYellow, store.UsageThresholdRed);
        UpdateTextBlock(_cpuTemperature, data.CpuTemp, "{0:F0}°C", store.TempThresholdYellow, store.TempThresholdRed);
        UpdateTextBlock(_cpuPower, data.CpuPower, "{0:F1} W");
        UpdateTextBlock(_cpuFanSpeed, data.CpuFanSpeed);

        UpdateTextBlock(_gpuFrequency, data.GpuFrequency, "{0} MHz");
        UpdateTextBlock(_gpuUsage, data.GpuUsage, "{0:F0}%", store.UsageThresholdYellow, store.UsageThresholdRed);
        UpdateTextBlock(_gpuTemperature, data.GpuTemp, "{0:F0}°C", store.TempThresholdYellow, store.TempThresholdRed);
        UpdateTextBlock(_gpuVramTemperature, data.GpuVramTemp, "{0:F0}°C", store.TempThresholdYellow, store.TempThresholdRed);
        UpdateTextBlock(_gpuPower, data.GpuPower, "{0:F1} W");
        UpdateTextBlock(_gpuFanSpeed, data.GpuFanSpeed);

        UpdateTextBlock(_memUsage, data.MemUsage, "{0:F0}%", store.UsageThresholdYellow, store.UsageThresholdRed);
        UpdateTextBlock(_memTemperature, data.MemTemp, "{0:F0}°C", store.TempThresholdYellow, store.TempThresholdRed);

        UpdateTextBlock(_pchTemperature, data.PchTemp, "{0:F0}°C", store.TempThresholdYellow, store.TempThresholdRed);
        UpdateTextBlock(_pchFanSpeed, data.PchFanSpeed);

        UpdateTextBlock(_disk0Temperature, data.Disk1Temp, "{0:F0}°C");
        UpdateTextBlock(_disk1Temperature, data.Disk2Temp, "{0:F0}°C");
    }
}