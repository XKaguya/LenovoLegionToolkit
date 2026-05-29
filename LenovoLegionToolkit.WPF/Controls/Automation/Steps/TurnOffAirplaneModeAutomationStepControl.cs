using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Automation.Steps;

public class TurnOffAirplaneModeAutomationStepControl : AbstractAutomationStepControl
{
    public TurnOffAirplaneModeAutomationStepControl(TurnOffAirplaneModeAutomationStep automationStep) : base(automationStep)
    {
        Icon = SymbolRegular.Airplane24;
        Title = Resource.TurnOffAirplaneModeAutomationStepControl_Title;
    }

    public override IAutomationStep CreateAutomationStep() => new TurnOffAirplaneModeAutomationStep();

    protected override UIElement? GetCustomControl() => null;

    protected override void OnFinishedLoading() { }

    protected override Task RefreshAsync() => Task.CompletedTask;
}
