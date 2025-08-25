using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.WPF.Windows;

public class BaseWindow : UiWindow
{
    protected BaseWindow()
    {
        SnapsToDevicePixels = true;
        ExtendsContentIntoTitleBar = true;

        WindowBackdropType = BackgroundType.Mica;

        DpiChanged += BaseWindow_DpiChanged;

        PreviewKeyDown += (s, e) => {
            if (e.Key == Key.System && e.SystemKey == Key.LeftAlt)
            {
                e.Handled = true;
                Keyboard.ClearFocus();
            }
        };  
    }

    private void BaseWindow_DpiChanged(object sender, DpiChangedEventArgs e) => VisualTreeHelper.SetRootDpi(this, e.NewDpi);
}
