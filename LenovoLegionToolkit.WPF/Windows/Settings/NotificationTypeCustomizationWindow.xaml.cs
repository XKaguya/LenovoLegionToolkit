using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.WPF.Controls;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using Wpf.Ui.Common;
using CardControl = LenovoLegionToolkit.WPF.Controls.Custom.CardControl;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

public partial class NotificationTypeCustomizationWindow
{
    private sealed class NotificationTypeRow(
        NotificationType type,
        SymbolRegularPickerControl iconPicker,
        ColorPickerControl iconColorPicker,
        ColorPickerControl textColorPicker,
        ComboBox positionComboBox,
        ComboBox durationComboBox)
    {
        public NotificationType Type { get; } = type;
        public SymbolRegularPickerControl IconPicker { get; } = iconPicker;
        public ColorPickerControl IconColorPicker { get; } = iconColorPicker;
        public ColorPickerControl TextColorPicker { get; } = textColorPicker;
        public ComboBox PositionComboBox { get; } = positionComboBox;
        public ComboBox DurationComboBox { get; } = durationComboBox;
    }

    private readonly ApplicationSettings _settings;
    private readonly IReadOnlyList<(NotificationType Type, string DisplayName)> _types;
    private readonly List<NotificationTypeRow> _rows = [];

    public NotificationTypeCustomizationWindow(
        string categoryTitle,
        IReadOnlyList<(NotificationType Type, string DisplayName)> types,
        ApplicationSettings settings)
    {
        _settings = settings;
        _types = types;

        InitializeComponent();

        Title = $"{Resource.Customize} {categoryTitle}";

        BuildRows();
    }

    private void BuildRows()
    {
        _cardsContainer.Children.Clear();
        _rows.Clear();

        foreach (var (type, displayName) in _types)
            _rows.Add(BuildRow(type, displayName));
    }

    private NotificationTypeRow BuildRow(NotificationType type, string displayName)
    {
        var notifications = _settings.Store.Notifications;

        var iconPicker = new SymbolRegularPickerControl
        {
            SelectedSymbol = notifications.IconOverrides.TryGetValue(type, out var iconInt)
                ? (SymbolRegular)iconInt
                : NotificationsManager.GetDefaultSymbol(type),
            Margin = new Thickness(0, 0, 8, 0)
        };
        iconPicker.SymbolChanged += (_, _) =>
        {
            if (iconPicker.SelectedSymbol.HasValue)
                notifications.IconOverrides[type] = (int)iconPicker.SelectedSymbol.Value;
            else
            {
                notifications.IconOverrides.Remove(type);
                iconPicker.SelectedSymbol = NotificationsManager.GetDefaultSymbol(type);
            }
            _settings.SynchronizeStore();
        };

        var iconColorPicker = new ColorPickerControl
        {
            SelectedColor = notifications.ColorOverrides.TryGetValue(type, out var iconColor)
                ? iconColor.ToColor()
                : Colors.White
        };
        iconColorPicker.ColorChangedDelayed += (_, _) =>
        {
            var c = iconColorPicker.SelectedColor;
            notifications.ColorOverrides[type] = new RGBColor(c.R, c.G, c.B);
            _settings.SynchronizeStore();
        };

        var textColorPicker = new ColorPickerControl
        {
            SelectedColor = notifications.TextColorOverrides.TryGetValue(type, out var textColor)
                ? textColor.ToColor()
                : Colors.White
        };
        textColorPicker.ColorChangedDelayed += (_, _) =>
        {
            var c = textColorPicker.SelectedColor;
            notifications.TextColorOverrides[type] = new RGBColor(c.R, c.G, c.B);
            _settings.SynchronizeStore();
        };

        var positionComboBox = BuildPositionComboBox(type);
        var durationComboBox = BuildDurationComboBox(type);

        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 0) };
        row1.Children.Add(iconPicker);
        row1.Children.Add(new TextBlock
        {
            Text = Resource.IconColor,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        });
        row1.Children.Add(iconColorPicker);
        row1.Children.Add(new TextBlock
        {
            Text = Resource.TextColor,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0)
        });
        row1.Children.Add(textColorPicker);

        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        row2.Children.Add(new TextBlock
        {
            Text = Resource.NotificationsSettingsWindow_NotificationPosition_Title,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        });
        row2.Children.Add(positionComboBox);
        row2.Children.Add(new TextBlock
        {
            Text = Resource.NotificationsSettingsWindow_NotificationDuration_Title,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0)
        });
        row2.Children.Add(durationComboBox);

        var contentPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        contentPanel.Children.Add(row1);
        contentPanel.Children.Add(row2);

        var card = new CardControl
        {
            Margin = new Thickness(0, 0, 0, 8),
            Header = new CardHeaderControl { Title = displayName },
            Content = contentPanel
        };
        _cardsContainer.Children.Add(card);

        return new NotificationTypeRow(type, iconPicker, iconColorPicker, textColorPicker, positionComboBox, durationComboBox);
    }

    private ComboBox BuildPositionComboBox(NotificationType type)
    {
        var comboBox = new ComboBox { MinWidth = 130, VerticalAlignment = VerticalAlignment.Center };

        comboBox.Items.Add(new ComboBoxItem { Content = Resource.Default, Tag = (NotificationPosition?)null });
        foreach (var pos in Enum.GetValues<NotificationPosition>())
            comboBox.Items.Add(new ComboBoxItem { Content = pos.GetDisplayName(), Tag = (NotificationPosition?)pos });

        var hasOverride = _settings.Store.Notifications.PositionOverrides.TryGetValue(type, out var posOverride);
        if (hasOverride)
        {
            var idx = Array.IndexOf(Enum.GetValues<NotificationPosition>(), posOverride);
            comboBox.SelectedIndex = idx + 1;
        }
        else
        {
            comboBox.SelectedIndex = 0;
        }

        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is ComboBoxItem { Tag: NotificationPosition pos })
                _settings.Store.Notifications.PositionOverrides[type] = pos;
            else
                _settings.Store.Notifications.PositionOverrides.Remove(type);
            _settings.SynchronizeStore();
        };

        return comboBox;
    }

    private ComboBox BuildDurationComboBox(NotificationType type)
    {
        var comboBox = new ComboBox { MinWidth = 100, VerticalAlignment = VerticalAlignment.Center };

        comboBox.Items.Add(new ComboBoxItem { Content = Resource.Default, Tag = (NotificationDuration?)null });
        foreach (var dur in Enum.GetValues<NotificationDuration>())
            comboBox.Items.Add(new ComboBoxItem { Content = dur.GetDisplayName(), Tag = (NotificationDuration?)dur });

        var hasOverride = _settings.Store.Notifications.DurationOverrides.TryGetValue(type, out var durOverride);
        if (hasOverride)
        {
            var idx = Array.IndexOf(Enum.GetValues<NotificationDuration>(), durOverride);
            comboBox.SelectedIndex = idx + 1;
        }
        else
        {
            comboBox.SelectedIndex = 0;
        }

        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is ComboBoxItem { Tag: NotificationDuration dur })
                _settings.Store.Notifications.DurationOverrides[type] = dur;
            else
                _settings.Store.Notifications.DurationOverrides.Remove(type);
            _settings.SynchronizeStore();
        };

        return comboBox;
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var notifications = _settings.Store.Notifications;
        foreach (var row in _rows)
        {
            notifications.IconOverrides.Remove(row.Type);
            notifications.ColorOverrides.Remove(row.Type);
            notifications.TextColorOverrides.Remove(row.Type);
            notifications.PositionOverrides.Remove(row.Type);
            notifications.DurationOverrides.Remove(row.Type);
        }
        _settings.SynchronizeStore();
        BuildRows();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

}
