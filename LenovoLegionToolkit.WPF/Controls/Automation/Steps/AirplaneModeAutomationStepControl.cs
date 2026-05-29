using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Automation.Steps;

public class AirplaneModeAutomationStepControl : AbstractComboBoxAutomationStepCardControl<ToggleState>
{
    public AirplaneModeAutomationStepControl(IAutomationStep<ToggleState> step) : base(step)
    {
        Icon = SymbolRegular.Airplane24;
        Title = Resource.AirplaneModeAutomationStepControl_Title;
    }
}
