using System.Windows.Controls;
using LenovoLegionToolkit.Lib;

namespace LenovoLegionToolkit.WPF.Controls.KeyboardBacklight.Spectrum.Device;

public class SpectrumKeyboardControl : UserControl
{
    private readonly SpectrumKeyboardANSIControl _ansi = new();
    private readonly SpectrumKeyboardISOControl _iso = new();
    private readonly SpectrumKeyboardJisControl _jis = new();
    private readonly SpectrumKeyboard24ZoneControl _keyboard24Zone = new();

    private readonly StackPanel _stackPanel = new();

    public SpectrumKeyboardControl()
    {
        Content = _stackPanel;
    }

    public void SetLayout(KeyboardLayout keyboardLayout)
    {
        _stackPanel.Children.Remove(_ansi);
        _stackPanel.Children.Remove(_iso);
        _stackPanel.Children.Remove(_jis);
        _stackPanel.Children.Remove(_keyboard24Zone);

        switch (keyboardLayout)
        {
            case KeyboardLayout.Ansi:
                _stackPanel.Children.Add(_ansi);
                break;
            case KeyboardLayout.Iso:
                _stackPanel.Children.Add(_iso);
                break;
            case KeyboardLayout.Jis:
                _stackPanel.Children.Add(_jis);
                break;
            case KeyboardLayout.Keyboard24Zone:
                _stackPanel.Children.Add(_keyboard24Zone);
                break;
        }

        UpdateLayout();
    }
}
