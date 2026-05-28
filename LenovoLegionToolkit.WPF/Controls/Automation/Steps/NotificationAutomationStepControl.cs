using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Windows.Settings;
using Wpf.Ui.Common;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace LenovoLegionToolkit.WPF.Controls.Automation.Steps;

public class NotificationAutomationStepControl : AbstractAutomationStepControl<NotificationAutomationStep>
{
    private readonly TextBox _scriptPath = new()
    {
        PlaceholderText = Resource.NotificationAutomationStepControl_NotificationText,
        Width = 300
    };

    private readonly Wpf.Ui.Controls.Button _customizeButton = new()
    {
        Icon = SymbolRegular.Edit24,
        ToolTip = Resource.Customize,
        MinWidth = 34,
        Height = 34,
        Margin = new Thickness(8, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly StackPanel _stackPanel = new() { Orientation = Orientation.Horizontal };

    public NotificationAutomationStepControl(NotificationAutomationStep step) : base(step)
    {
        Icon = SymbolRegular.Rocket24;
        Title = Resource.NotificationAutomationStepControl_Title;

        SizeChanged += RunAutomationStepControl_SizeChanged;
    }

    private void RunAutomationStepControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged)
            return;

        var newWidth = e.NewSize.Width / 3;
        _scriptPath.Width = newWidth;
    }

    public override IAutomationStep CreateAutomationStep() => new NotificationAutomationStep(_scriptPath.Text)
    {
        IconOverride = AutomationStep.IconOverride,
        ColorOverride = AutomationStep.ColorOverride,
        TextColorOverride = AutomationStep.TextColorOverride,
        PositionOverride = AutomationStep.PositionOverride,
        DurationOverride = AutomationStep.DurationOverride
    };

    protected override UIElement GetCustomControl()
    {
        _scriptPath.TextChanged += (_, _) =>
        {
            if (_scriptPath.Text != AutomationStep.Text)
                RaiseChanged();
        };

        _customizeButton.Click += CustomizeButton_Click;

        _stackPanel.Children.Add(_scriptPath);
        _stackPanel.Children.Add(_customizeButton);

        return _stackPanel;
    }

    private void CustomizeButton_Click(object sender, RoutedEventArgs e)
    {
        var proxyStore = new StepCustomizationStore(AutomationStep, RaiseChanged);
        var types = new[] { (NotificationType.AutomationNotification, Resource.NotificationAutomationStepControl_Title) };
        new NotificationTypeCustomizationWindow(
            Resource.NotificationAutomationStepControl_Title,
            types,
            proxyStore)
        {
            Owner = Window.GetWindow(this)
        }.ShowDialog();
    }

    protected override void OnFinishedLoading() { }

    protected override Task RefreshAsync()
    {
        _scriptPath.Text = AutomationStep.Text ?? string.Empty;
        return Task.CompletedTask;
    }

    private class StepCustomizationStore : INotificationCustomizationStore
    {
        private readonly NotificationAutomationStep _step;
        private readonly Action _raiseChanged;

        public Dictionary<NotificationType, int> IconOverrides { get; } = [];
        public Dictionary<NotificationType, RGBColor> ColorOverrides { get; } = [];
        public Dictionary<NotificationType, RGBColor> TextColorOverrides { get; } = [];
        public Dictionary<NotificationType, NotificationPosition> PositionOverrides { get; } = [];
        public Dictionary<NotificationType, NotificationDuration> DurationOverrides { get; } = [];

        public StepCustomizationStore(NotificationAutomationStep step, Action raiseChanged)
        {
            _step = step;
            _raiseChanged = raiseChanged;

            if (step.IconOverride.HasValue) IconOverrides[NotificationType.AutomationNotification] = step.IconOverride.Value;
            if (step.ColorOverride.HasValue) ColorOverrides[NotificationType.AutomationNotification] = step.ColorOverride.Value;
            if (step.TextColorOverride.HasValue) TextColorOverrides[NotificationType.AutomationNotification] = step.TextColorOverride.Value;
            if (step.PositionOverride.HasValue) PositionOverrides[NotificationType.AutomationNotification] = step.PositionOverride.Value;
            if (step.DurationOverride.HasValue) DurationOverrides[NotificationType.AutomationNotification] = step.DurationOverride.Value;
        }

        public void SynchronizeStore()
        {
            _step.IconOverride = IconOverrides.TryGetValue(NotificationType.AutomationNotification, out var icon) ? icon : null;
            _step.ColorOverride = ColorOverrides.TryGetValue(NotificationType.AutomationNotification, out var color) ? color : null;
            _step.TextColorOverride = TextColorOverrides.TryGetValue(NotificationType.AutomationNotification, out var textColor) ? textColor : null;
            _step.PositionOverride = PositionOverrides.TryGetValue(NotificationType.AutomationNotification, out var pos) ? pos : null;
            _step.DurationOverride = DurationOverrides.TryGetValue(NotificationType.AutomationNotification, out var dur) ? dur : null;

            _raiseChanged();
        }
    }
}
