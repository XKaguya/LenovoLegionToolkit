using System;
using LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;

namespace LenovoLegionToolkit.WPF.Windows.Automation.TabItemContent;

public partial class PeriodicAutomationPipelineTriggerTabItemContent : IAutomationPipelineTriggerTabItemContent<IPeriodicAutomationPipelineTrigger>
{
    private readonly IPeriodicAutomationPipelineTrigger _trigger;

    public PeriodicAutomationPipelineTriggerTabItemContent(IPeriodicAutomationPipelineTrigger trigger)
    {
        _trigger = trigger;
        InitializeComponent();
    }

    private void PeriodicAutomationPipelineTriggerTabItemContent_Initialized(object? sender, EventArgs e)
    {
        var totalSeconds = _trigger.Period.TotalSeconds;

        if (totalSeconds > 0 && (totalSeconds < 60 || totalSeconds % 60 != 0))
        {
            _unitComboBox.SelectedIndex = 1;
            _periodValuePicker.Value = totalSeconds;
            _periodValuePicker.Maximum = 86400;
        }
        else
        {
            _unitComboBox.SelectedIndex = 0;
            _periodValuePicker.Value = _trigger.Period.TotalMinutes;
            _periodValuePicker.Maximum = 1440;
        }
    }

    public IPeriodicAutomationPipelineTrigger GetTrigger()
    {
        var value = _periodValuePicker.Value ?? 1;

        TimeSpan period = _unitComboBox.SelectedIndex == 1
            ? TimeSpan.FromSeconds(value)
            : TimeSpan.FromMinutes(value);

        return _trigger.DeepCopy(period);
    }
}
