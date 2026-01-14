using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Automation.Steps;

public class CloseAutomationStepControl : AbstractAutomationStepControl
{
    public CloseAutomationStepControl(CloseAutomationStep automationStep) : base(automationStep)
    {
        Icon = SymbolRegular.ArrowExit20;
        Title = Resource.CloseAutomationStepControl_Title;
        Subtitle = Resource.CloseAutomationStepControl_Message;
    }

    public override IAutomationStep CreateAutomationStep() => new CloseAutomationStep();

    protected override UIElement? GetCustomControl() => null;

    protected override void OnFinishedLoading() { }

    protected override Task RefreshAsync() => Task.CompletedTask;
}
