using LenovoLegionToolkit.Lib;

namespace LenovoLegionToolkit.WPF.Controls.KeyboardBacklight.Spectrum.Device;

public partial class SpectrumDeviceFullControl
{
    public SpectrumDeviceFullControl()
    {
        InitializeComponent();

        // Future codes for Ambient Aft RGB Zone.
        //var mi = Compatibility.GetMachineInformationAsync().Result;
        //if ((mi.LegionSeries == LegionSeries.Legion_Pro_7 || mi.LegionSeries == LegionSeries.Legion_9) && mi.Generation == 10)
        //{
        //    _ambient1.Visibility = Visibility.Hidden;
        //    _ambient2.Visibility = Visibility.Hidden;
        //    _ambient3.Visibility = Visibility.Hidden;
        //    _ambient4.Visibility = Visibility.Hidden;
        //    _ambient5.Visibility = Visibility.Hidden;
        //    _ambient6.Visibility = Visibility.Hidden;
        //    _ambient7.Visibility = Visibility.Hidden;
        //    _ambient8.Visibility = Visibility.Hidden;
        //}
    }

    public void SetLayout(KeyboardLayout keyboardLayout)
    {
        _keyboard.SetLayout(keyboardLayout);
    }
}
