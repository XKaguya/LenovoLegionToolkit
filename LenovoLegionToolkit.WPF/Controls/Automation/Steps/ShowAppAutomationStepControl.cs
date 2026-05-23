using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Automation.Steps;

public class ShowAppAutomationStepControl : AbstractAutomationStepControl
{
    public ShowAppAutomationStepControl(ShowAppAutomationStep automationStep) : base(automationStep)
    {
        Icon = SymbolRegular.Window20;
        Title = Resource.ShowAppAutomationStepControl_Title;
        Subtitle = Resource.ShowAppAutomationStepControl_Message;
    }

    public override IAutomationStep CreateAutomationStep() => new ShowAppAutomationStep();

    protected override UIElement? GetCustomControl() => null;

    protected override void OnFinishedLoading() { }

    protected override Task RefreshAsync() => Task.CompletedTask;
}
