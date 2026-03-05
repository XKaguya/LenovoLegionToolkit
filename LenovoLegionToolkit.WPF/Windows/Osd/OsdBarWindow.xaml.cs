using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.WPF.Resources;
using System.Windows.Media;

namespace LenovoLegionToolkit.WPF.Windows.Osd;

public partial class OsdBarWindow : OsdWindowBase
{
    private Style? _originalTextBlockStyle;

    public OsdBarWindow()
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

        _gadgetGroups = new()
        {
            { _fpsGroup, ([OsdItem.Fps, OsdItem.LowFps, OsdItem.FrameTime], _separatorFps) },
            { _cpuGroup, ([OsdItem.CpuUtilization, OsdItem.CpuFrequency, OsdItem.CpuPCoreFrequency, OsdItem.CpuECoreFrequency, OsdItem.CpuTemperature, OsdItem.CpuPower, OsdItem.CpuFan], _separatorCpu) },
            { _gpuGroup, ([OsdItem.GpuUtilization, OsdItem.GpuFrequency, OsdItem.GpuTemperature, OsdItem.GpuVramTemperature, OsdItem.GpuPower, OsdItem.GpuFan], _separatorGpu) },
            { _memoryGroup, ([OsdItem.MemoryUtilization, OsdItem.MemoryTemperature], _separatorMemory) },
            { _pchGroup, ([OsdItem.PchTemperature, OsdItem.PchFan, OsdItem.Disk1Temperature, OsdItem.Disk2Temperature], null) }
        };

        if (Resources["TextBlockStyle"] is Style style)
            _originalTextBlockStyle = style;

        InitOsd();
    }

    protected override double? SavedPositionX
    {
        get => _OsdSettings.Store.BarPositionX;
        set => _OsdSettings.Store.BarPositionX = value;
    }

    protected override double? SavedPositionY
    {
        get => _OsdSettings.Store.BarPositionY;
        set => _OsdSettings.Store.BarPositionY = value;
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
            _backgroundBorder.Background = new SolidColorBrush(color);
        }
        catch
        {
            _backgroundBorder.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x20, 0x20, 0x20));
        }

        double fontSize = _OsdSettings.Store.FontSize;
        if (_originalTextBlockStyle != null)
        {
            var newStyle = new Style(typeof(TextBlock), _originalTextBlockStyle);
            newStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, fontSize));
            Resources["TextBlockStyle"] = newStyle;
        }

        if (_labelBrush != null)
        {
            _fpsLabel.Foreground = _labelBrush;
            _cpuLabel.Foreground = _labelBrush;
            _gpuLabel.Foreground = _labelBrush;
            _memLabel.Foreground = _labelBrush;
            _pchName.Foreground = _labelBrush;
        }
        else
        {
            var converter = new BrushConverter();
            _fpsLabel.Foreground = (Brush)converter.ConvertFromString("#2196F3")!;
            _cpuLabel.Foreground = (Brush)converter.ConvertFromString("#FF5722")!;
            _gpuLabel.Foreground = (Brush)converter.ConvertFromString("#4CAF50")!;
            _memLabel.Foreground = (Brush)converter.ConvertFromString("#FFC107")!;
            _pchName.Foreground = (Brush)converter.ConvertFromString("#9C27B0")!;
        }

        ApplyCornerRadius(_backgroundBorder);
    }

    protected override void SetDefaultWindowPosition()
    {
        if (double.IsNaN(ActualWidth) || ActualWidth <= 0) return;

        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var workArea = SystemParameters.WorkArea;

        Left = (screenWidth - ActualWidth) / 2;
        Top = workArea.Top;
        _positionSet = true;
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

        UpdateTextBlock(_cpuFrequency, data.CpuFrequency, "{0}MHz");
        UpdateTextBlock(_cpuPFrequency, data.CpuPClock, "{0:F0}MHz");
        UpdateTextBlock(_cpuEFrequency, data.CpuEClock, "{0:F0}MHz");
        UpdateTextBlock(_cpuUsage, data.CpuUsage, "{0:F0}%", store.UsageThresholdYellow, store.UsageThresholdRed);
        UpdateTextBlock(_cpuTemperature, data.CpuTemp, "{0:F0}°C", store.TempThresholdYellow, store.TempThresholdRed);
        UpdateTextBlock(_cpuPower, data.CpuPower, "{0:F1}W");
        UpdateTextBlock(_cpuFanSpeed, data.CpuFanSpeed, "RPM");

        UpdateTextBlock(_gpuFrequency, data.GpuFrequency, "{0}MHz");
        UpdateTextBlock(_gpuUsage, data.GpuUsage, "{0:F0}%", store.UsageThresholdYellow, store.UsageThresholdRed);
        UpdateTextBlock(_gpuTemperature, data.GpuTemp, "{0:F0}°C", store.TempThresholdYellow, store.TempThresholdRed);
        UpdateTextBlock(_gpuVramTemperature, data.GpuVramTemp, "{0:F0}°C", store.TempThresholdYellow, store.TempThresholdRed);
        UpdateTextBlock(_gpuPower, data.GpuPower, "{0:F1}W");
        UpdateTextBlock(_gpuFanSpeed, data.GpuFanSpeed, "RPM");

        UpdateTextBlock(_memUsage, data.MemUsage, "{0:F0}%", store.UsageThresholdYellow, store.UsageThresholdRed);
        UpdateTextBlock(_memTemperature, data.MemTemp, "{0:F0}°C", store.TempThresholdYellow, store.TempThresholdRed);

        UpdateTextBlock(_pchTemperature, data.PchTemp, "{0:F0}°C", store.TempThresholdYellow, store.TempThresholdRed);
        UpdateTextBlock(_pchFanSpeed, data.PchFanSpeed, "RPM");

        UpdateTextBlock(_disk0Temperature, data.Disk1Temp, "{0:F0}°C");
        UpdateTextBlock(_disk1Temperature, data.Disk2Temp, "{0:F0}°C");
    }
}