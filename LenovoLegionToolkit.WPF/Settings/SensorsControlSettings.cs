using LenovoLegionToolkit.Lib.Settings;

namespace LenovoLegionToolkit.WPF.Settings;

public class SensorsControlSettings() : AbstractSettings<SensorsControlSettings.SensorsControlSettingsStore>("sensors.json")
{
    public class SensorsControlSettingsStore
    {
        public bool ShowSensors { get; set; } = true;
        public int SensorsRefreshIntervalSeconds { get; set; } = 1;
        public SensorGroup[]? Groups { get; set; } = SensorGroup.DefaultGroups;
        public SensorItem[]? VisibleItems { get; set; } = SensorGroup.DefaultGroups[0].Items;
    }

    public void Reset()
    {
        Store = Default;
    }

    protected override SensorsControlSettingsStore Default => new();
}
