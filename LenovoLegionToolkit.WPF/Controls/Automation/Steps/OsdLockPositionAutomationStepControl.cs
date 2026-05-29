using LenovoLegionToolkit.Lib.Automation;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Automation.Steps;

public class OsdLockPositionAutomationStepControl : AbstractComboBoxAutomationStepCardControl<OsdLockPositionAutomationStepState>
{
    public OsdLockPositionAutomationStepControl(IAutomationStep<OsdLockPositionAutomationStepState> step) : base(step)
    {
        Icon = SymbolRegular.LockClosed24;
        Title = Resource.OsdLockPositionAutomationStepControl_Title;
        Subtitle = Resource.OsdLockPositionAutomationStepControl_Message;
    }
}
