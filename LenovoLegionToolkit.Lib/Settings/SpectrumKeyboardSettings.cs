namespace LenovoLegionToolkit.Lib.Settings;

public class SpectrumKeyboardSettings()
    : AbstractSettings<SpectrumKeyboardSettings.SpectrumKeyboardSettingsStore>("spectrum_keyboard.json")
{
    public class SpectrumKeyboardSettingsStore
    {
        public KeyboardLayout? KeyboardLayout { get; set; }
        public bool AuroraVantageColorBoost { get; set; }
        public int AuroraVantageColorBoostFloor { get; set; } = 20;
        public int AuroraVantageColorBoostTarget { get; set; } = 80;
        public int AuroraVantageColorBoostWhite { get; set; } = 224;
        public int AuroraVantageColorBoostBrightnessFactor { get; set; } = 50;
    }
}
