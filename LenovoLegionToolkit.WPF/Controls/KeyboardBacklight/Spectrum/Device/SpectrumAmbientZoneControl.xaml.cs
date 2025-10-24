using System.Linq;
using System.Windows.Media;

namespace LenovoLegionToolkit.WPF.Controls.KeyboardBacklight.Spectrum.Device;

public partial class SpectrumAmbientZoneControl
{
    private ushort[] _keyCodes = Enumerable.Range(1001, 18).Select(i => (ushort)i).ToArray();

    public ushort[] KeyCodes
    {
        get => _keyCodes;
        set
        {
            _keyCodes = value;

#if DEBUG
            _button.ToolTip = string.Join(", ", value.Select(v => $"0x{v:X4}"));
#endif
        }
    }

    public Color? Color
    {
        get => (_background.Background as SolidColorBrush)?.Color;
        set
        {
            if (!value.HasValue)
                _background.Background = null;
            else if (_background.Background is SolidColorBrush brush)
                brush.Color = value.Value;
            else
                _background.Background = new SolidColorBrush(value.Value);
        }
    }

    public bool? IsChecked
    {
        get => _button.IsChecked;
        set => _button.IsChecked = value;
    }

    public SpectrumAmbientZoneControl()
    {
        InitializeComponent();
    }
}
