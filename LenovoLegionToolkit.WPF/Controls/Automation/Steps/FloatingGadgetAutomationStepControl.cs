using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Automation.Steps;

public class FloatingGadgetAutomationStepControl : AbstractComboBoxAutomationStepCardControl<FloatingGadgetState>
{
    public FloatingGadgetAutomationStepControl(IAutomationStep<FloatingGadgetState> step) : base(step)
    {
        Icon = SymbolRegular.Window16;
        Title = Resource.FloatingGadgetAutomationStepControl_Title;
        Subtitle = Resource.FloatingGadgetAutomationStepControl_Message;
    }
}
