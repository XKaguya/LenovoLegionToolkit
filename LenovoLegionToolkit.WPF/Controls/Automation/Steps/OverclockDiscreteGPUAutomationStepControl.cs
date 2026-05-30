using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Automation.Steps;

public class OverclockDiscreteGPUAutomationStepControl : AbstractComboBoxAutomationStepCardControl<ToggleState>
{
    public OverclockDiscreteGPUAutomationStepControl(IAutomationStep<ToggleState> step) : base(step)
    {
        Icon = SymbolRegular.DeveloperBoardLightning20;
        Title = Resource.OverclockDiscreteGPUAutomationStepControl_Title;
        Subtitle = Resource.OverclockDiscreteGPUAutomationStepControl_Message;
    }
}
