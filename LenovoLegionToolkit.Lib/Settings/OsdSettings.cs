using System;
using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Settings;

public class OsdSettings() : AbstractSettings<OsdSettings.OsdSettingsStore>("osd.json")
{
    public class OsdSettingsStore
    {
        public bool ShowOsd { get; set; }
        public double OsdRefreshInterval { get; set; } = 1;
        public int SelectedStyleIndex { get; set; } = 0;
        public List<OsdItem> Items { get; set; } = Enum.GetValues<OsdItem>().ToList();

        public double BackgroundOpacity { get; set; } = 0.6;
        public string BackgroundColor { get; set; } = "#1E1E1E";
        public int FontSize { get; set; } = 12;
        public int CornerRadiusTop { get; set; } = 6;
        public int CornerRadiusBottom { get; set; } = 6;
        public bool IsLocked { get; set; } = false;
        public double? PanelPositionX { get; set; }
        public double? PanelPositionY { get; set; }
        public double? BarPositionX { get; set; }
        public double? BarPositionY { get; set; }

        public int TempThresholdYellow { get; set; } = 75;
        public int TempThresholdRed { get; set; } = 90;
        public int UsageThresholdYellow { get; set; } = 70;
        public int UsageThresholdRed { get; set; } = 90;
        public int FpsThresholdRed { get; set; } = 30;
        public int LowFpsDeltaThreshold { get; set; } = 30;

        public OsdColorSource LabelColorSource { get; set; } = OsdColorSource.Default;
        public string? LabelColor { get; set; }
        public string WarningColor { get; set; } = "#FFFF00";
        public string CriticalColor { get; set; } = "#FF0000";
    }
}
