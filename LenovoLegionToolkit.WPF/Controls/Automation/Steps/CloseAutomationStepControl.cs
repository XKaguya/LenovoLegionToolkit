using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Automation.Steps;

public class CloseAutomationStepControl : AbstractComboBoxAutomationStepCardControl<Close>
{
    public CloseAutomationStepControl(IAutomationStep<Close> step) : base(step)
    {
        Icon = SymbolRegular.ArrowExit20;
        Title = Resource.CloseAutomationStepControl_Title;
        Subtitle = Resource.CloseAutomationStepControl_Message;
    }
}
