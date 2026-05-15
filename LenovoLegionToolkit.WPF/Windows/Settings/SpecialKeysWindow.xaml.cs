using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

        var displayedCodes = new HashSet<int>();

        foreach (var key in Enum.GetValues<SpecialKey>())
        {
            if (key is not (SpecialKey.FnF9 or SpecialKey.FnPrtSc or SpecialKey.FnPrtSc2
                or SpecialKey.FnR or SpecialKey.FnR2 or SpecialKey.FnN
                or SpecialKey.FnF4 or SpecialKey.FnF8))
                continue;

            var code = (int)key;
            displayedCodes.Add(code);
            AddKeyCard(code, GetSpecialKeyDisplayName(code));
        }

        foreach (var key in Enum.GetValues<DriverKey>())
        {
            var code = (int)key;
            if (displayedCodes.Contains(code))
                continue;

            displayedCodes.Add(code);
            AddKeyCard(code, GetDriverKeyDisplayName(key));
        }

        foreach (var (code, name) in _settings.Store.KeyDescriptions)
        {
            if (displayedCodes.Contains(code))
                continue;
            if (Enum.IsDefined(typeof(SpecialKey), (SpecialKey)code))
                continue;
            if (Enum.IsDefined(typeof(DriverKey), (DriverKey)code))
                continue;

            AddKeyCard(code, name);
        }
    }

    private void AddKeyCard(int code, string displayName)
    {
        var mode = _settings.Store.KeyModes.TryGetValue(code, out var m)
            ? m : CustomSpecialKey.Default;

        var subtitleText = mode == CustomSpecialKey.Default
            ? Resource.SpecialKey_Mode_Default_Description
            : Resource.SpecialKey_Mode_Custom_Description;

        var isCustom = !Enum.IsDefined(typeof(SpecialKey), (SpecialKey)code)
            && !Enum.IsDefined(typeof(DriverKey), (DriverKey)code);

        var card = new CardAction
        {
            Margin = new(0, 0, 0, 4),
            Icon = SymbolRegular.Keyboard24,
            Content = new CardHeaderControl
            {
                Title = displayName,
                Subtitle = subtitleText
            },
            Tag = code,
            Cursor = System.Windows.Input.Cursors.Hand,
            ContextMenu = isCustom ? CreateDeleteContextMenu(code) : null
        };
        card.Click += (_, _) => OpenKeyDetailWindow(code, displayName);

        _keyList.Items.Add(card);
    }

    private ContextMenu CreateDeleteContextMenu(int code)
    {
        var deleteItem = new MenuItem { Header = Resource.Delete };
        deleteItem.Click += (_, _) =>
        {
            _settings.Store.KeyDescriptions.Remove(code);
            _settings.Store.KeyModes.Remove(code);
            _settings.Store.KeyActions.Remove(code);
            _settings.SynchronizeStore();
            BuildKeyList();
        };

        return new ContextMenu { Items = { deleteItem } };
    }

    private void OpenKeyDetailWindow(int keyCode, string displayName)
    {
        var window = new SpecialKeyDetailWindow(keyCode, displayName) { Owner = this };
        window.ShowDialog();
        BuildKeyList();
    }

    private string GetSpecialKeyDisplayName(int code)
    {
        if (_settings.Store.KeyDescriptions.TryGetValue(code, out var customName))
            return customName;

        if (Enum.IsDefined(typeof(SpecialKey), (SpecialKey)code))
        {
            var key = (SpecialKey)code;
            string str = key.ToString();
            if (str.StartsWith("Fn", StringComparison.OrdinalIgnoreCase) && str.Length > 2)
                return string.Concat("Fn ", str.AsSpan(2));
            return str;
        }

        return $"Key {code}";
    }

    private static string GetDriverKeyDisplayName(DriverKey key)
    {
        string str = key.ToString();
        if (str.StartsWith("Fn", StringComparison.OrdinalIgnoreCase) && str.Length > 2)
            return string.Concat("Fn ", str.AsSpan(2), " (Driver)");
        return $"{str} (Driver)";
    }
}
