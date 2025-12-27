using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;
using NumberBox = Wpf.Ui.Controls.NumberBox;

namespace LenovoLegionToolkit.WPF.Controls.Automation.Steps;

public class SpeakerVolumeAutomationStepControl : AbstractAutomationStepControl<SpeakerVolumeAutomationStep>
{
    public SpeakerVolumeAutomationStepControl(SpeakerVolumeAutomationStep step) : base(step)
    {
        Icon = SymbolRegular.Speaker224;
        Title = Resource.SpeakerVolumeAutomationStepControl_Title;
        Subtitle = Resource.SpeakerVolumeAutomationStepControl_Message;
    }

    private readonly NumberBox _volume = new()
    {
        Width = 130,
        ClearButtonEnabled = false,
        MaxDecimalPlaces = 0,
        Minimum = 0,
        Maximum = 100,
        SmallChange = 1,
        LargeChange = 10
    };

    private readonly StackPanel _container = new()
    {
        Orientation = Orientation.Horizontal
    };

    public override IAutomationStep CreateAutomationStep() => new SpeakerVolumeAutomationStep((int?)_volume.Value ?? 0);

    protected override UIElement GetCustomControl()
    {
        _volume.ValueChanged += (_, _) =>
        {
            if ((int?)_volume.Value != AutomationStep.Volume)
                RaiseChanged();
        };

        _container.Children.Clear();
        _container.Children.Add(_volume);

        return _container;
    }

    protected override void OnFinishedLoading() { }

    protected override Task RefreshAsync()
    {
        _volume.Value = AutomationStep.Volume;
        return Task.CompletedTask;
    }
}