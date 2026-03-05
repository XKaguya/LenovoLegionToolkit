using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Automation.Steps;

public class OsdAutomationStepControl : AbstractComboBoxAutomationStepCardControl<OsdState>
{
    public OsdAutomationStepControl(IAutomationStep<OsdState> step) : base(step)
    {
        Icon = SymbolRegular.Window16;
        Title = Resource.OsdAutomationStepControl_Title;
        Subtitle = Resource.OsdAutomationStepControl_Message;
    }
}
