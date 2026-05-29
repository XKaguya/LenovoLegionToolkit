using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Automation.Steps;

public class WiFiAutomationStepControl : AbstractComboBoxAutomationStepCardControl<ToggleState>
{
    public WiFiAutomationStepControl(IAutomationStep<ToggleState> step) : base(step)
    {
        Icon = SymbolRegular.Wifi124;
        Title = Resource.WiFiAutomationStepControl_Title;
    }
}
