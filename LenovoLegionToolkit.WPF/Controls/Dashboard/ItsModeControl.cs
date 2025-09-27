using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows.Utils;
using Wpf.Ui.Common;
using Button = Wpf.Ui.Controls.Button;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public class ItsModeControl : AbstractComboBoxFeatureCardControl<ITSMode>
{
    private readonly ItsModeFeature _itsModeFeature = IoCContainer.Resolve<ItsModeFeature>();

    private readonly Button _configButton = new()
    {
        Icon = SymbolRegular.Settings24,
        FontSize = 20,
        Margin = new(8, 0, 0, 0),
        Visibility = Visibility.Collapsed,
    };

    public ItsModeControl()
    {
        Icon = SymbolRegular.Gauge24;
        Title = Resource.PowerModeControl_Title;
        Subtitle = Resource.PowerModeControl_Message;

        AutomationProperties.SetName(_configButton, Resource.PowerModeControl_Title);
    }

    protected override async Task OnRefreshAsync()
    {
        await base.OnRefreshAsync();
    }

    protected override async Task OnStateChangeAsync(ComboBox comboBox, IFeature<ITSMode> feature, ITSMode? newValue, ITSMode? oldValue)
    {
        await base.OnStateChangeAsync(comboBox, feature, newValue, oldValue);

        var mi = await Compatibility.GetMachineInformationAsync();

        if (newValue == null || oldValue == null)
            return;

        if (newValue.Value != oldValue.Value)
        {
            try
            {
                await _itsModeFeature.SetStateAsync(newValue.Value);
                _itsModeFeature.LastItsMode = newValue.Value;
            }
            catch (DllNotFoundException ex)
            {
                var dialog = new DialogWindow
                {
                    Title = "Missing compoment",
                    Content = "PowerBattery.dll is missing. Please place it manually to use ITSMode feature.",
                    Owner = App.Current.MainWindow
                };

                dialog.ShowDialog();
            }
        }
    }

    protected override void OnStateChangeException(Exception exception)
    {
        if (exception is PowerModeUnavailableWithoutACException ex1)
        {
            SnackbarHelper.Show(Resource.PowerModeUnavailableWithoutACException_Title,
                string.Format(Resource.PowerModeUnavailableWithoutACException_Message, ex1.PowerMode.GetDisplayName()),
                SnackbarType.Warning);
        }
    }
}
