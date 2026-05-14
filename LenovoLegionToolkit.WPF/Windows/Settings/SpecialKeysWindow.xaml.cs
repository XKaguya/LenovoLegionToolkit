using System;
using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.WPF.Controls;
using LenovoLegionToolkit.WPF.Controls.Custom;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

public partial class SpecialKeysWindow
{
    private readonly SpecialKeySettings _settings = IoCContainer.Resolve<SpecialKeySettings>();

    public SpecialKeysWindow()
    {
        InitializeComponent();
        Title = _title.Text = Resource.SettingsPage_Category_SmartKeys;
        IsVisibleChanged += SpecialKeysWindow_IsVisibleChanged;
    }

    private async void SpecialKeysWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
            await RefreshAsync();
    }

    private Task RefreshAsync()
    {
        BuildKeyList();
        return Task.CompletedTask;
    }

    private void BuildKeyList()
    {
        _keyList.Items.Clear();

        foreach (var key in Enum.GetValues<SpecialKey>())
        {
            if (key is not (SpecialKey.FnF9 or SpecialKey.FnPrtSc or SpecialKey.FnPrtSc2
                or SpecialKey.FnR or SpecialKey.FnR2 or SpecialKey.FnN
                or SpecialKey.FnF4 or SpecialKey.FnF8))
                continue;

            var keyInt = (int)key;
            var mode = _settings.Store.KeyModes.TryGetValue(keyInt, out var m)
                ? m : CustomSpecialKey.Default;

            var subtitleText = mode == CustomSpecialKey.Default
                ? Resource.SpecialKey_Mode_Default_Description
                : Resource.SpecialKey_Mode_Custom_Description;

            var keyCopy = key;
            var card = new CardAction
            {
                Margin = new(0, 0, 0, 4),
                Icon = SymbolRegular.Keyboard24,
                Content = new CardHeaderControl
                {
                    Title = GetSpecialKeyDisplayName(key),
                    Subtitle = subtitleText
                },
                Tag = keyInt,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            card.Click += (_, _) => OpenKeyDetailWindow(keyCopy);

            _keyList.Items.Add(card);
        }
    }

    private void KeyDiscovery_Click(object sender, RoutedEventArgs e)
    {
        var window = new KeyDiscoveryWindow { Owner = this };
        window.ShowDialog();
    }

    private void OpenKeyDetailWindow(SpecialKey key)
    {
        var window = new SpecialKeyDetailWindow(key) { Owner = this };
        window.ShowDialog();
        BuildKeyList();
    }

    private string GetSpecialKeyDisplayName(SpecialKey key)
    {
        string str = key.ToString();
        if (str.StartsWith("Fn", StringComparison.OrdinalIgnoreCase) && str.Length > 2)
        {
            return string.Concat("Fn ", str.AsSpan(2));
        }
        return str;
    }
}
