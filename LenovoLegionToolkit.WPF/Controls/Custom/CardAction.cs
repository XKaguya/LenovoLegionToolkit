using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using UiCardAction = Wpf.Ui.Controls.CardAction;

namespace LenovoLegionToolkit.WPF.Controls.Custom;

public class CardAction : UiCardAction
{
    static CardAction()
    {
        MarginProperty.OverrideMetadata(
            typeof(CardAction),
            new FrameworkPropertyMetadata(
                new Thickness(0),
                FrameworkPropertyMetadataOptions.AffectsMeasure,
                null,
                (_, v) => v is Thickness m && CardControl.IsCompact
                    ? new Thickness(m.Left, m.Top, m.Right, m.Bottom / 2)
                    : v));
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new CardActionAutomationPeer(this);

    private class CardActionAutomationPeer(CardAction owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(CardAction);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

        public override object? GetPattern(PatternInterface patternInterface)
        {
            if (patternInterface == PatternInterface.ItemContainer)
                return this;

            return base.GetPattern(patternInterface);
        }

        protected override AutomationPeer? GetLabeledByCore()
        {
            if (owner.Content is UIElement element)
                return CreatePeerForElement(element);

            return base.GetLabeledByCore();
        }

        protected override string GetNameCore()
        {
            var result = base.GetNameCore() ?? string.Empty;

            if (result == string.Empty)
                result = AutomationProperties.GetName(owner);

            if (result == string.Empty && owner.Content is DependencyObject d)
                result = AutomationProperties.GetName(d);

            if (result == string.Empty && owner.Content is string s)
                result = s;

            return result;
        }
    }
}
