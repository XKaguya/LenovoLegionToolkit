using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Automation.Steps;

public class ITSModeAutomationStepControl : AbstractComboBoxAutomationStepCardControl<ITSMode>
{
    public ITSModeAutomationStepControl(IAutomationStep<ITSMode> step) : base(step)
    {
        Icon = SymbolRegular.Gauge24;
        Title = Resource.ITSModeAutomationStepControl_Title;
        Subtitle = Resource.ITSModeAutomationStepControl_Message;
    }
}
