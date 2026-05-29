using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Automation.Steps;

public class CycleGodModePresetAutomationStepControl : AbstractAutomationStepControl
{
    public CycleGodModePresetAutomationStepControl(CycleGodModePresetAutomationStep automationStep) : base(automationStep)
    {
        Icon = SymbolRegular.ArrowSync24;
        Title = Resource.CycleGodModePresetAutomationStepControl_Title;
        Subtitle = Resource.CycleGodModePresetAutomationStepControl_Message;
    }

    public override IAutomationStep CreateAutomationStep() => new CycleGodModePresetAutomationStep();

    protected override UIElement? GetCustomControl() => null;

    protected override void OnFinishedLoading() { }

    protected override Task RefreshAsync() => Task.CompletedTask;
}
