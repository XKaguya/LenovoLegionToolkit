using System;
using Windows.Devices.Lights;
using Color = Windows.UI.Color;
using System.Collections.Generic;

namespace LenovoLegionToolkit.Lib.Utils.LampEffects;

public class AuroraSyncEffect : ILampEffect
{
    public string Name => "Aurora Sync";
    public Dictionary<string, object> Parameters { get; } = new();

    private RGBColor[,]? _screenColors;
    private int _width;
    private int _height;

    private double _centerX, _centerY, _widthX, _heightY;

    public void SetBounds(double centerX, double centerY, double widthX, double heightY)
    {
        _centerX = centerX;
        _centerY = centerY;
        _widthX = widthX;
        _heightY = heightY;
        
        if (_widthX < 0.01) _widthX = 0.45;
        if (_heightY < 0.01) _heightY = 0.15;
    }

    public void UpdateScreenData(RGBColor[,] colors, int width, int height)
    {
        _screenColors = colors;
        _width = width;
        _height = height;
    }

    public void Reset() { }

    public Color GetColorForLamp(int lampIndex, double time, LampInfo lampInfo, int totalLamps)
    {
        if (_screenColors == null || _width == 0 || _height == 0) return Color.FromArgb(0, 0, 0, 0);

        double dx = lampInfo.Position.X - _centerX;
        double dy = lampInfo.Position.Y - _centerY;

        double u = 0.5 + (dx / _widthX);
        double v = 0.5 + (dy / _heightY);

        u = Math.Clamp(u, 0, 0.999);
        v = Math.Clamp(v, 0, 0.999);

        int px = (int)(u * _width);
        int py = (int)(v * _height);

        var color = _screenColors[px, py];
        return Color.FromArgb(255, color.R, color.G, color.B);
    }
}
