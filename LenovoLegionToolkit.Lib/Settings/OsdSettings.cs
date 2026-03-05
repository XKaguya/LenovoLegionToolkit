using System.Collections.Generic;
using System.IO;
using LenovoLegionToolkit.Lib.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static LenovoLegionToolkit.Lib.Settings.OsdSettings;

namespace LenovoLegionToolkit.Lib.Settings;

public class OsdSettings() : AbstractSettings<OsdSettingsStore>("floating_gadget.json")
{
    public class OsdSettingsStore
    {
        [JsonProperty("ShowFloatingGadgets")]
        public bool ShowOsd { get; set; }

        [JsonProperty("FloatingGadgetsRefreshInterval")]
        public double OsdRefreshInterval { get; set; } = 1;
        public int SelectedStyleIndex { get; set; } = 0;
        public List<OsdItem> Items { get; set; } = [];

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

    public override OsdSettingsStore? LoadStore()
    {
        var store = base.LoadStore();
        if (store != null)
            return store;

        try
        {
            var oldSettingsPath = Path.Combine(Folders.AppData, "settings.json");
            if (!File.Exists(oldSettingsPath))
                return null;

            var json = File.ReadAllText(oldSettingsPath);
            var jObject = JsonConvert.DeserializeObject<JObject>(json, JsonSerializerSettings);
            var itemsToken = jObject?["OsdItems"];
            
            var migrated = new OsdSettingsStore();

            if (jObject?["ShowFloatingGadgets"] is { } showToken)
            {
                migrated.ShowOsd = showToken.ToObject<bool>();
            }

            if (jObject?["FloatingGadgetsRefreshInterval"] is { } intervalToken)
            {
                migrated.OsdRefreshInterval = intervalToken.ToObject<double>();
                if (migrated.OsdRefreshInterval < 0.1)
                    migrated.OsdRefreshInterval = 0.1;
            }

            if (jObject?["SelectedStyleIndex"] is { } styleToken)
            {
                migrated.SelectedStyleIndex = styleToken.ToObject<int>();
            }

            if (itemsToken != null)
            {
                var items = itemsToken.ToObject<List<OsdItem>>();
                if (items is { Count: > 0 })
                    migrated.Items = items;
            }

            Store = migrated;
            Save();

            return migrated;
        }
        catch
        {
            return null;
        }
    }
}
