using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Automation.Steps;

public class CloseAppAutomationStepControl : AbstractAutomationStepControl
{
    public CloseAppAutomationStepControl(CloseAppAutomationStep automationStep) : base(automationStep)
    {
        Icon = SymbolRegular.ArrowExit20;
        Title = Resource.CloseAppAutomationStepControl_Title;
        Subtitle = Resource.CloseAppAutomationStepControl_Message;
    }

    public override IAutomationStep CreateAutomationStep() => new CloseAppAutomationStep();

    protected override UIElement? GetCustomControl() => null;

    protected override void OnFinishedLoading() { }

    protected override Task RefreshAsync() => Task.CompletedTask;
}
