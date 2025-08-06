using LenovoLegionToolkit.Lib;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace LenovoLegionToolkit.WPF.Windows.Dashboard;

public partial class ExtendedHybridModeInfoWindow
{
    public ExtendedHybridModeInfoWindow(HybridModeState[] hybridModeStates)
    {
        InitializeComponent();

        _hybridPanel.Visibility = hybridModeStates.Contains(HybridModeState.On)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _hybridIgpuPanel.Visibility = hybridModeStates.Contains(HybridModeState.OnIGPUOnly)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _hybridAutoPanel.Visibility = hybridModeStates.Contains(HybridModeState.OnAuto)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _dgpuPanel.Visibility = hybridModeStates.Contains(HybridModeState.Off)
            ? Visibility.Visible
            : Visibility.Collapsed;

        PreviewKeyDown += (s, e) => {
            if (e.Key == Key.System && e.SystemKey == Key.LeftAlt)
            {
                e.Handled = true;
                Keyboard.ClearFocus();
            }
        };
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
