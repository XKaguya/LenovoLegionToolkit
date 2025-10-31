﻿using LenovoLegionToolkit.Lib.Settings;
using System.Linq;

namespace LenovoLegionToolkit.WPF.Settings;

public class SensorsControlSettings() : AbstractSettings<SensorsControlSettings.SensorsControlSettingsStore>("sensors.json")
{
    public class SensorsControlSettingsStore
    {
        public bool ShowSensors { get; set; } = true;
        public int SensorsRefreshIntervalSeconds { get; set; } = 1;
        public SensorGroup[]? Groups { get; set; } = SensorGroup.DefaultGroups;
        public SensorItem[]? VisibleItems { get; set; } = SensorGroup.DefaultGroups.SelectMany(group => group.Items).ToArray();
    }

    public new void Reset()
    {
        Store = Default;
    }

    protected override SensorsControlSettingsStore Default => new();
}
