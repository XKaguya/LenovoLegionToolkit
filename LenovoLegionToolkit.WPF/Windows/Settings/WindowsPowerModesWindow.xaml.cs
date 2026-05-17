using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.WPF.Controls;
using LenovoLegionToolkit.WPF.Controls.Custom;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using static LenovoLegionToolkit.Lib.Settings.GodModeSettings;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

public partial class WindowsPowerModesWindow
{
    private readonly PowerModeFeature _powerModeFeature = IoCContainer.Resolve<PowerModeFeature>();
    private readonly ITSModeFeature _itsModeFeature = IoCContainer.Resolve<ITSModeFeature>();
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly GodModeController _godModeController = IoCContainer.Resolve<GodModeController>();
    private readonly GodModeSettings _godModeSettings = IoCContainer.Resolve<GodModeSettings>();

    private bool IsRefreshing => _loader.IsLoading;

    public WindowsPowerModesWindow()
    {
        InitializeComponent();
        IsVisibleChanged += PowerModesWindow_IsVisibleChanged;
    }

    private async void PowerModesWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            await RefreshAsync();
        }
    }

    private async Task RefreshAsync()
    {
        _loader.IsLoading = true;

        var loadingTask = Task.Delay(500);

        _cardsContainer.Children.Clear();

        var powerModes = Enum.GetValues<WindowsPowerMode>();

        var isPowerModeSupported = await _powerModeFeature.IsSupportedAsync();
        if (isPowerModeSupported)
        {
            var allStates = await _powerModeFeature.GetAllStatesAsync();

            foreach (var state in allStates)
            {
                if (state == PowerModeState.GodMode)
                {
                    await BuildGodModeCardAsync(powerModes);
                }
                else
                {
                    BuildModeCard(powerModes, state, state.GetDisplayName(), (mode, isAc) => WindowsPowerModeAcDcChangedAsync(mode, state, isAc));
                }
            }
        }
        else
        {
            var isITSModeSupported = await _itsModeFeature.IsSupportedAsync();
            if (isITSModeSupported)
            {
                var itsModes = await _itsModeFeature.GetAllStatesAsync();
                foreach (var itsMode in itsModes.Where(m => m != ITSMode.None))
                {
                    BuildITSModeCard(powerModes, itsMode);
                }
            }
        }

        await loadingTask;
        _loader.IsLoading = false;
    }

    private void BuildModeCard(WindowsPowerMode[] powerModes, PowerModeState powerModeState, string title,
        Func<WindowsPowerMode, bool, Task> onChanged)
    {
        var defaultMode = _settings.Store.PowerModes.GetValueOrDefault(powerModeState, WindowsPowerMode.Balanced);
        var savedAc = _settings.Store.Overrides.GetPowerModeOnAc(powerModeState);
        var savedDc = _settings.Store.Overrides.GetPowerModeOnDc(powerModeState);

        var card = new CardControl
        {
            Margin = new Thickness(0, 0, 0, 8),
            Header = new CardHeaderControl { Title = title }
        };

        var (row, acCombo, dcCombo) = BuildAcDcRow(powerModes, savedAc ?? defaultMode, savedDc ?? defaultMode);

        acCombo.SelectionChanged += async (_, _) =>
        {
            if (acCombo.TryGetSelectedItem(out WindowsPowerMode mode))
            {
                await onChanged(mode, true);
            }
        };
        dcCombo.SelectionChanged += async (_, _) =>
        {
            if (dcCombo.TryGetSelectedItem(out WindowsPowerMode mode))
            {
                await onChanged(mode, false);
            }
        };

        card.Content = row;
        _cardsContainer.Children.Add(card);
    }

    private void BuildITSModeCard(WindowsPowerMode[] powerModes, ITSMode itsMode)
    {
        var defaultMode = _settings.Store.ITSPowerModes.GetValueOrDefault(itsMode, WindowsPowerMode.Balanced);
        var savedAc = _settings.Store.ITSOverrides.GetPowerModeOnAc(itsMode);
        var savedDc = _settings.Store.ITSOverrides.GetPowerModeOnDc(itsMode);

        var card = new CardControl
        {
            Margin = new Thickness(0, 0, 0, 8),
            Header = new CardHeaderControl { Title = itsMode.GetDisplayName() }
        };

        var (row, acCombo, dcCombo) = BuildAcDcRow(powerModes, savedAc ?? defaultMode, savedDc ?? defaultMode);

        acCombo.SelectionChanged += async (_, _) =>
        {
            if (acCombo.TryGetSelectedItem(out WindowsPowerMode mode))
            {
                await ITSModeAcDcChangedAsync(itsMode, mode, isAc: true);
            }
        };
        dcCombo.SelectionChanged += async (_, _) =>
        {
            if (dcCombo.TryGetSelectedItem(out WindowsPowerMode mode))
            {
                await ITSModeAcDcChangedAsync(itsMode, mode, isAc: false);
            }
        };

        card.Content = row;
        _cardsContainer.Children.Add(card);
    }

    private async Task BuildGodModeCardAsync(WindowsPowerMode[] powerModes)
    {
        var controller = await _godModeController.GetControllerAsync().ConfigureAwait(false);
        var presets = await controller.GetGodModePresetsAsync().ConfigureAwait(false);

        if (presets.Count > 1)
        {
            var card = new CardControl
            {
                Margin = new Thickness(0, 0, 0, 8),
                Header = new CardHeaderControl { Title = PowerModeState.GodMode.GetDisplayName() }
            };

            var contentStack = new StackPanel();

            foreach (var preset in presets)
            {
                var savedAc = preset.Value.Overrides.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerModeOnAc)
                    ?? _settings.Store.PowerModes.GetValueOrDefault(PowerModeState.GodMode, WindowsPowerMode.Balanced);
                var savedDc = preset.Value.Overrides.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerModeOnDc)
                    ?? _settings.Store.PowerModes.GetValueOrDefault(PowerModeState.GodMode, WindowsPowerMode.Balanced);

                var (row, acCombo, dcCombo) = BuildPresetRow(powerModes, savedAc, savedDc, preset.Value.Name);

                var presetKey = preset.Key.ToString();
                acCombo.SelectionChanged += async (_, _) =>
                {
                    if (acCombo.TryGetSelectedItem(out WindowsPowerMode mode))
                    {
                        await GodModePresetPowerModeChangedAsync(presetKey, mode, isAc: true);
                    }
                };
                dcCombo.SelectionChanged += async (_, _) =>
                {
                    if (dcCombo.TryGetSelectedItem(out WindowsPowerMode mode))
                    {
                        await GodModePresetPowerModeChangedAsync(presetKey, mode, isAc: false);
                    }
                };

                contentStack.Children.Add(row);
            }

            card.Content = contentStack;
            _cardsContainer.Children.Add(card);
        }
        else
        {
            var singlePreset = presets.FirstOrDefault();
            var defaultMode = _settings.Store.PowerModes.GetValueOrDefault(PowerModeState.GodMode, WindowsPowerMode.Balanced);
            var savedAc = singlePreset.Value?.Overrides.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerModeOnAc) ?? defaultMode;
            var savedDc = singlePreset.Value?.Overrides.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerModeOnDc) ?? defaultMode;

            var presetKey = singlePreset.Key.ToString();
            BuildModeCard(powerModes, PowerModeState.GodMode, singlePreset.Value?.Name ?? PowerModeState.GodMode.GetDisplayName(),
                async (mode, isAc) => await GodModePresetPowerModeChangedAsync(presetKey, mode, isAc));
        }
    }

    private static (StackPanel Row, ComboBox AcCombo, ComboBox DcCombo) BuildAcDcRow(
        WindowsPowerMode[] powerModes, WindowsPowerMode savedAc, WindowsPowerMode savedDc)
    {
        var grayBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

        var grid = new Grid
        {
            Margin = new Thickness(0, 6, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "AcLabel" },
                new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "AcCombo" },
                new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "DcLabel" },
                new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "DcCombo" },
            }
        };

        var acLabel = new TextBlock
        {
            Text = Resource.WindowsPowerPlansWindow_PowerMode_AC,
            FontSize = 11,
            Foreground = grayBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };
        Grid.SetColumn(acLabel, 0);
        var acCombo = new ComboBox { Width = 140, MaxDropDownHeight = 300 };
        Grid.SetColumn(acCombo, 1);
        acCombo.SetItems(powerModes, savedAc, pm => pm.GetDisplayName());

        var dcLabel = new TextBlock
        {
            Text = Resource.WindowsPowerPlansWindow_PowerMode_DC,
            FontSize = 11,
            Foreground = grayBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 4, 0)
        };
        Grid.SetColumn(dcLabel, 2);
        var dcCombo = new ComboBox { Width = 140, MaxDropDownHeight = 300 };
        Grid.SetColumn(dcCombo, 3);
        dcCombo.SetItems(powerModes, savedDc, pm => pm.GetDisplayName());

        grid.Children.Add(acLabel);
        grid.Children.Add(acCombo);
        grid.Children.Add(dcLabel);
        grid.Children.Add(dcCombo);

        var row = new StackPanel();
        row.Children.Add(grid);

        return (row, acCombo, dcCombo);
    }

    private static (StackPanel Row, ComboBox AcCombo, ComboBox DcCombo) BuildPresetRow(
        WindowsPowerMode[] powerModes, WindowsPowerMode savedAc, WindowsPowerMode savedDc, string presetName)
    {
        var grayBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

        var grid = new Grid
        {
            Margin = new Thickness(0, 4, 0, 4),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "AcLabel" },
                new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "AcCombo" },
                new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "DcLabel" },
                new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "DcCombo" },
            }
        };

        var nameLabel = new TextBlock
        {
            Text = presetName,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(nameLabel, 0);

        var acLabel = new TextBlock
        {
            Text = Resource.WindowsPowerPlansWindow_PowerMode_AC,
            FontSize = 11,
            Foreground = grayBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };
        Grid.SetColumn(acLabel, 1);
        var acCombo = new ComboBox { Width = 140, MaxDropDownHeight = 300 };
        Grid.SetColumn(acCombo, 2);
        acCombo.SetItems(powerModes, savedAc, pm => pm.GetDisplayName());

        var dcLabel = new TextBlock
        {
            Text = Resource.WindowsPowerPlansWindow_PowerMode_DC,
            FontSize = 11,
            Foreground = grayBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 4, 0)
        };
        Grid.SetColumn(dcLabel, 3);
        var dcCombo = new ComboBox { Width = 140, MaxDropDownHeight = 300 };
        Grid.SetColumn(dcCombo, 4);
        dcCombo.SetItems(powerModes, savedDc, pm => pm.GetDisplayName());

        grid.Children.Add(nameLabel);
        grid.Children.Add(acLabel);
        grid.Children.Add(acCombo);
        grid.Children.Add(dcLabel);
        grid.Children.Add(dcCombo);

        var row = new StackPanel();
        row.Children.Add(grid);

        return (row, acCombo, dcCombo);
    }

    private async Task WindowsPowerModeAcDcChangedAsync(WindowsPowerMode windowsPowerMode, PowerModeState powerModeState, bool isAc)
    {
        if (IsRefreshing)
        {
            return;
        }

        if (isAc)
        {
            _settings.Store.Overrides.SetPowerModeOnAc(powerModeState, windowsPowerMode);
        }
        else
        {
            _settings.Store.Overrides.SetPowerModeOnDc(powerModeState, windowsPowerMode);
        }

        _settings.SynchronizeStore();

        var currentState = await _powerModeFeature.GetStateAsync();
        if (currentState == powerModeState)
        {
            await _powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync();
        }
    }

    private async Task ITSModeAcDcChangedAsync(ITSMode itsMode, WindowsPowerMode windowsPowerMode, bool isAc)
    {
        if (IsRefreshing)
        {
            return;
        }

        if (isAc)
        {
            _settings.Store.ITSOverrides.SetPowerModeOnAc(itsMode, windowsPowerMode);
        }
        else
        {
            _settings.Store.ITSOverrides.SetPowerModeOnDc(itsMode, windowsPowerMode);
        }

        _settings.SynchronizeStore();

        var currentState = await _itsModeFeature.GetStateAsync();
        if (currentState == itsMode)
        {
            var controller = IoCContainer.Resolve<WindowsPowerModeController>();
            await controller.SetPowerModeAsync(itsMode);
        }
    }

    private async Task GodModePresetPowerModeChangedAsync(string presetKey, WindowsPowerMode windowsPowerMode, bool isAc)
    {
        if (IsRefreshing)
        {
            return;
        }

        var presetKvp = _godModeSettings.Store.Presets.FirstOrDefault(profile => profile.Key.ToString() == presetKey);

        if (!presetKvp.Equals(default(KeyValuePair<Guid, GodModeSettingsStore.Preset>)) && presetKvp.Value != null)
        {
            var newOv = new Dictionary<PowerOverrideKey, string>(presetKvp.Value.Overrides ?? []);
            if (isAc)
            {
                newOv.SetEnum(PowerOverrideKey.PowerModeOnAc, (WindowsPowerMode?)windowsPowerMode);
            }
            else
            {
                newOv.SetEnum(PowerOverrideKey.PowerModeOnDc, (WindowsPowerMode?)windowsPowerMode);
            }
            var updated = presetKvp.Value with { Overrides = newOv };
            _godModeSettings.Store.Presets[presetKvp.Key] = updated;
            _godModeSettings.SynchronizeStore();

            var currentState = await _powerModeFeature.GetStateAsync();
            if (currentState == PowerModeState.GodMode && _godModeSettings.Store.ActivePresetId.ToString() == presetKey)
            {
                await _powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync(updated);
            }
        }
    }
}
