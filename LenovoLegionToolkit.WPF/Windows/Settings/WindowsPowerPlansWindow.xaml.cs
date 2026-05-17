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
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Controls;
using LenovoLegionToolkit.WPF.Controls.Custom;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using static LenovoLegionToolkit.Lib.Settings.GodModeSettings;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

public partial class WindowsPowerPlansWindow
{
    private static readonly WindowsPowerPlan DefaultValue = new(Guid.Empty, Resource.WindowsPowerPlansWindow_DefaultPowerPlan, false);

    private readonly WindowsPowerPlanController _windowsPowerPlanController = IoCContainer.Resolve<WindowsPowerPlanController>();
    private readonly PowerModeFeature _powerModeFeature = IoCContainer.Resolve<PowerModeFeature>();
    private readonly ITSModeFeature _itsModeFeature = IoCContainer.Resolve<ITSModeFeature>();
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly GodModeController _godModeController = IoCContainer.Resolve<GodModeController>();
    private readonly GodModeSettings _godModeSettings = IoCContainer.Resolve<GodModeSettings>();

    private bool IsRefreshing => _loader.IsLoading;

    public WindowsPowerPlansWindow()
    {
        InitializeComponent();
        IsVisibleChanged += PowerPlansWindow_IsVisibleChanged;
    }

    private async void PowerPlansWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
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

        var compatibility = await Compatibility.GetMachineInformationAsync();
        _aoAcWarningCard.Visibility = compatibility.Properties.SupportsAlwaysOnAc.status
            ? Visibility.Visible
            : Visibility.Collapsed;

        _cardsContainer.Children.Clear();

        var isPowerModeSupported = await _powerModeFeature.IsSupportedAsync();

        if (isPowerModeSupported)
        {
            var powerPlans = _windowsPowerPlanController.GetPowerPlans().OrderBy(x => x.Name).Prepend(DefaultValue).ToArray();
            var powerModes = Enum.GetValues<WindowsPowerMode>();
            var allStates = await _powerModeFeature.GetAllStatesAsync();

            foreach (var state in allStates)
            {
                if (state == PowerModeState.GodMode)
                {
                    await BuildGodModeCardsAsync(powerPlans, powerModes);
                }
                else
                {
                    BuildPowerPlanCard(powerPlans, powerModes, state, state.GetDisplayName());
                }
            }
        }
        else
        {
            var isITSModeSupported = await _itsModeFeature.IsSupportedAsync();
            if (isITSModeSupported)
            {
                var powerPlans = _windowsPowerPlanController.GetPowerPlans().OrderBy(x => x.Name).Prepend(DefaultValue).ToArray();
                var powerModes = Enum.GetValues<WindowsPowerMode>();
                var itsModes = await _itsModeFeature.GetAllStatesAsync();

                foreach (var itsMode in itsModes.Where(m => m != ITSMode.None))
                {
                    BuildITSPowerPlanCard(powerPlans, powerModes, itsMode);
                }
            }
        }

        await loadingTask;
        _loader.IsLoading = false;
    }

    private void BuildPowerPlanCard(WindowsPowerPlan[] powerPlans, WindowsPowerMode[] powerModes,
        PowerModeState state, string title,
        Guid? savedPlan = null, WindowsPowerMode? savedAc = null, WindowsPowerMode? savedDc = null,
        Func<WindowsPowerPlan, Task>? onPlanChanged = null,
        Func<WindowsPowerMode, bool, Task>? onOverlayChanged = null)
    {
        Guid settingsPowerPlanGuid;
        if (savedPlan.HasValue)
        {
            settingsPowerPlanGuid = savedPlan.Value;
        }
        else if (!_settings.Store.PowerPlans.TryGetValue(state, out settingsPowerPlanGuid))
        {
            settingsPowerPlanGuid = Guid.Empty;
        }
        var selectedPlan = powerPlans.FirstOrDefault(pp => pp.Guid == settingsPowerPlanGuid);
        var effectivePlan = (selectedPlan == default) ? DefaultValue : selectedPlan;

        var card = new CardControl
        {
            Margin = new Thickness(0, 0, 0, 8),
            Header = new CardHeaderControl { Title = title }
        };

        var comboBox = new ComboBox
        {
            MinWidth = 200,
            Margin = new Thickness(0, 0, 0, 8),
            MaxDropDownHeight = 300
        };
        comboBox.SetItems(powerPlans, effectivePlan, pp => pp.Name);

        var overlayContainer = new StackPanel();

        var ac = savedAc ?? (_settings.Store.Overrides.GetPowerPlanBalanceOnAc(state) ?? WindowsPowerMode.Balanced);
        var dc = savedDc ?? (_settings.Store.Overrides.GetPowerPlanBalanceOnDc(state) ?? WindowsPowerMode.Balanced);
        var (overlayRow, acCombo, dcCombo) = BuildBalanceOverlayRow(powerModes, ac, dc);

        acCombo.SelectionChanged += async (_, _) =>
        {
            if (acCombo.TryGetSelectedItem(out WindowsPowerMode mode))
            {
                if (onOverlayChanged != null)
                {
                    await onOverlayChanged(mode, true);
                }
                else
                {
                    await BalanceOverlayChangedAsync(mode, state, isAc: true);
                }
            }
        };
        dcCombo.SelectionChanged += async (_, _) =>
        {
            if (dcCombo.TryGetSelectedItem(out WindowsPowerMode mode))
            {
                if (onOverlayChanged != null)
                {
                    await onOverlayChanged(mode, false);
                }
                else
                {
                    await BalanceOverlayChangedAsync(mode, state, isAc: false);
                }
            }
        };

        overlayContainer.Children.Add(overlayRow);
        overlayContainer.Visibility = IsBalancedPlan(effectivePlan.Guid) ? Visibility.Visible : Visibility.Collapsed;

        comboBox.SelectionChanged += async (_, _) =>
        {
            if (comboBox.TryGetSelectedItem(out WindowsPowerPlan plan))
            {
                overlayContainer.Visibility = IsBalancedPlan(plan.Guid) ? Visibility.Visible : Visibility.Collapsed;
                if (onPlanChanged != null)
                {
                    await onPlanChanged(plan);
                }
                else
                {
                    await WindowsPowerPlanChangedAsync(plan, state);
                }
            }
        };

        var stackPanel = new StackPanel();
        stackPanel.Children.Add(comboBox);
        stackPanel.Children.Add(overlayContainer);

        card.Content = stackPanel;
        _cardsContainer.Children.Add(card);
    }

    private void BuildITSPowerPlanCard(WindowsPowerPlan[] powerPlans, WindowsPowerMode[] powerModes, ITSMode itsMode)
    {
        var settingsPowerPlanGuid = _settings.Store.ITSPowerPlans.GetValueOrDefault(itsMode);
        var selectedPlan = powerPlans.FirstOrDefault(pp => pp.Guid == settingsPowerPlanGuid);
        var effectivePlan = (selectedPlan == default) ? DefaultValue : selectedPlan;

        var card = new CardControl
        {
            Margin = new Thickness(0, 0, 0, 8),
            Header = new CardHeaderControl { Title = itsMode.GetDisplayName() }
        };

        var comboBox = new ComboBox
        {
            MinWidth = 200,
            Margin = new Thickness(0, 0, 0, 8),
            MaxDropDownHeight = 300
        };
        comboBox.SetItems(powerPlans, effectivePlan, pp => pp.Name);

        var overlayContainer = new StackPanel();

        var ac = _settings.Store.ITSOverrides.GetPowerPlanBalanceOnAc(itsMode) ?? WindowsPowerMode.Balanced;
        var dc = _settings.Store.ITSOverrides.GetPowerPlanBalanceOnDc(itsMode) ?? WindowsPowerMode.Balanced;
        var (overlayRow, acCombo, dcCombo) = BuildBalanceOverlayRow(powerModes, ac, dc);

        acCombo.SelectionChanged += async (_, _) =>
        {
            if (acCombo.TryGetSelectedItem(out WindowsPowerMode mode))
            {
                await ITSBalanceOverlayChangedAsync(itsMode, mode, isAc: true);
            }
        };
        dcCombo.SelectionChanged += async (_, _) =>
        {
            if (dcCombo.TryGetSelectedItem(out WindowsPowerMode mode))
            {
                await ITSBalanceOverlayChangedAsync(itsMode, mode, isAc: false);
            }
        };

        overlayContainer.Children.Add(overlayRow);
        overlayContainer.Visibility = IsBalancedPlan(effectivePlan.Guid) ? Visibility.Visible : Visibility.Collapsed;

        comboBox.SelectionChanged += async (_, _) =>
        {
            if (comboBox.TryGetSelectedItem(out WindowsPowerPlan plan))
            {
                overlayContainer.Visibility = IsBalancedPlan(plan.Guid) ? Visibility.Visible : Visibility.Collapsed;
                await ITSPlanChangedAsync(itsMode, plan);
            }
        };

        var stackPanel = new StackPanel();
        stackPanel.Children.Add(comboBox);
        stackPanel.Children.Add(overlayContainer);

        card.Content = stackPanel;
        _cardsContainer.Children.Add(card);
    }

    private async Task BuildGodModeCardsAsync(WindowsPowerPlan[] powerPlans, WindowsPowerMode[] powerModes)
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
                var presetContent = BuildGodModePresetContent(powerPlans, powerModes, preset);
                contentStack.Children.Add(presetContent);
            }

            card.Content = contentStack;
            _cardsContainer.Children.Add(card);
        }
        else
        {
            var singlePreset = presets.FirstOrDefault();
            BuildPowerPlanCard(powerPlans, powerModes, PowerModeState.GodMode,
                singlePreset.Value?.Name ?? PowerModeState.GodMode.GetDisplayName(),
                savedPlan: singlePreset.Value?.Overrides.TryGetGuid(PowerOverrideKey.PowerPlan),
                savedAc: singlePreset.Value?.Overrides.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerPlanBalanceOnAc),
                savedDc: singlePreset.Value?.Overrides.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerPlanBalanceOnDc),
                onPlanChanged: async (plan) => await GodModePresetPowerPlanChangedAsync(singlePreset.Key.ToString(), plan),
                onOverlayChanged: async (mode, isAc) => await GodModePresetBalanceOverlayChangedAsync(singlePreset.Key.ToString(), mode, isAc));
        }
    }

    private static (StackPanel Row, ComboBox AcCombo, ComboBox DcCombo) BuildBalanceOverlayRow(
        WindowsPowerMode[] powerModes, WindowsPowerMode savedAc, WindowsPowerMode savedDc)
    {
        var grayBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0)
        };

        var title = new TextBlock
        {
            Text = Resource.WindowsPowerPlansWindow_PowerMode_Title,
            FontSize = 11,
            Foreground = grayBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };

        var acLabel = new TextBlock
        {
            Text = Resource.WindowsPowerPlansWindow_PowerMode_AC,
            FontSize = 11,
            Foreground = grayBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };
        var acCombo = new ComboBox { Width = 140, MaxDropDownHeight = 300 };
        acCombo.SetItems(powerModes, savedAc, pm => pm.GetDisplayName());

        var dcLabel = new TextBlock
        {
            Text = Resource.WindowsPowerPlansWindow_PowerMode_DC,
            FontSize = 11,
            Foreground = grayBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 4, 0)
        };
        var dcCombo = new ComboBox { Width = 140, MaxDropDownHeight = 300 };
        dcCombo.SetItems(powerModes, savedDc, pm => pm.GetDisplayName());

        row.Children.Add(title);
        row.Children.Add(acLabel);
        row.Children.Add(acCombo);
        row.Children.Add(dcLabel);
        row.Children.Add(dcCombo);

        return (row, acCombo, dcCombo);
    }

    private StackPanel BuildGodModePresetContent(WindowsPowerPlan[] powerPlans, WindowsPowerMode[] powerModes,
        KeyValuePair<Guid, GodModeSettingsStore.Preset> preset)
    {
        var container = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

        var headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            }
        };

        var nameLabel = new TextBlock
        {
            Text = preset.Value.Name,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(nameLabel, 0);

        var comboBox = new ComboBox
        {
            MinWidth = 200,
            MaxDropDownHeight = 300
        };

        var currentPowerPlanGuid = GetGodModePresetPowerPlan(preset.Key.ToString());
        var selectedPlanGuid = currentPowerPlanGuid ?? Guid.Empty;
        var selectedPlan = powerPlans.FirstOrDefault(pp => pp.Guid == selectedPlanGuid);
        var effectivePlan = (selectedPlan == default) ? DefaultValue : selectedPlan;
        comboBox.SetItems(powerPlans, effectivePlan, pp => pp.Name);
        Grid.SetColumn(comboBox, 1);

        headerGrid.Children.Add(nameLabel);
        headerGrid.Children.Add(comboBox);
        container.Children.Add(headerGrid);

        var savedAc = preset.Value.Overrides.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerPlanBalanceOnAc) ?? WindowsPowerMode.Balanced;
        var savedDc = preset.Value.Overrides.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerPlanBalanceOnDc) ?? WindowsPowerMode.Balanced;
        var (overlayRow, acCombo, dcCombo) = BuildBalanceOverlayRow(powerModes, savedAc, savedDc);
        overlayRow.Visibility = IsBalancedPlan(effectivePlan.Guid) ? Visibility.Visible : Visibility.Collapsed;
        container.Children.Add(overlayRow);

        comboBox.SelectionChanged += async (_, _) =>
        {
            if (comboBox.TryGetSelectedItem(out WindowsPowerPlan plan))
            {
                overlayRow.Visibility = IsBalancedPlan(plan.Guid) ? Visibility.Visible : Visibility.Collapsed;
                await GodModePresetPowerPlanChangedAsync(preset.Key.ToString(), plan);
            }
        };
        acCombo.SelectionChanged += async (_, _) =>
        {
            if (acCombo.TryGetSelectedItem(out WindowsPowerMode mode))
            {
                await GodModePresetBalanceOverlayChangedAsync(preset.Key.ToString(), mode, isAc: true);
            }
        };
        dcCombo.SelectionChanged += async (_, _) =>
        {
            if (dcCombo.TryGetSelectedItem(out WindowsPowerMode mode))
            {
                await GodModePresetBalanceOverlayChangedAsync(preset.Key.ToString(), mode, isAc: false);
            }
        };

        return container;
    }

    private static bool IsBalancedPlan(Guid guid) =>
        PowerPlanExtensions.IsPlanBasedOnBalanced(guid);

    private Guid? GetGodModePresetPowerPlan(string presetKey)
    {
        if (Guid.TryParse(presetKey, out var presetGuid) &&
            _godModeSettings.Store.Presets.TryGetValue(presetGuid, out var preset))
        {
            var powerPlanGuid = preset.Overrides.TryGetGuid(PowerOverrideKey.PowerPlan);
            if (powerPlanGuid != null)
            {
                return powerPlanGuid;
            }
        }
        if (!_settings.Store.PowerPlans.TryGetValue(PowerModeState.GodMode, out var globalGuid))
        {
            return Guid.Empty;
        }
        return globalGuid;
    }

    private async Task WindowsPowerPlanChangedAsync(WindowsPowerPlan windowsPowerPlan, PowerModeState powerModeState, GodModeSettingsStore.Preset? preset = null)
    {
        if (IsRefreshing)
        {
            return;
        }

        if (preset == null)
        {
            _settings.Store.PowerPlans[powerModeState] = windowsPowerPlan.Guid;
            if (windowsPowerPlan.Guid != Guid.Empty && !IsBalancedPlan(windowsPowerPlan.Guid))
            {
                _settings.Store.Overrides.SetPowerPlanBalanceOnAc(powerModeState, null);
                _settings.Store.Overrides.SetPowerPlanBalanceOnDc(powerModeState, null);
            }
        }
        else
        {
            var powerPlan = preset.Overrides.TryGetGuid(PowerOverrideKey.PowerPlan);
            if (powerPlan == null)
            {
                return;
            }
        }

        _settings.SynchronizeStore();

        var currentState = await _powerModeFeature.GetStateAsync();
        if (currentState == powerModeState)
        {
            if (powerModeState == PowerModeState.GodMode && preset != null)
            {
                if (_godModeSettings.Store.Presets.TryGetValue(_godModeSettings.Store.ActivePresetId, out var activePreset) &&
                    !activePreset.Equals(preset))
                {
                    return;
                }
            }
            await _powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync(preset);
        }
    }

    private async Task BalanceOverlayChangedAsync(WindowsPowerMode selectedMode, PowerModeState powerModeState, bool isAc)
    {
        if (IsRefreshing)
        {
            return;
        }

        if (isAc)
        {
            _settings.Store.Overrides.SetPowerPlanBalanceOnAc(powerModeState, selectedMode);
        }
        else
        {
            _settings.Store.Overrides.SetPowerPlanBalanceOnDc(powerModeState, selectedMode);
        }

        _settings.SynchronizeStore();

        var currentState = await _powerModeFeature.GetStateAsync();
        if (currentState == powerModeState)
        {
            await _powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync();
        }
    }

    private async Task ITSPlanChangedAsync(ITSMode itsMode, WindowsPowerPlan windowsPowerPlan)
    {
        if (IsRefreshing)
        {
            return;
        }

        _settings.Store.ITSPowerPlans[itsMode] = windowsPowerPlan.Guid;
        if (windowsPowerPlan.Guid != Guid.Empty && !IsBalancedPlan(windowsPowerPlan.Guid))
        {
            _settings.Store.ITSOverrides.SetPowerPlanBalanceOnAc(itsMode, null);
            _settings.Store.ITSOverrides.SetPowerPlanBalanceOnDc(itsMode, null);
        }

        _settings.SynchronizeStore();

        var currentState = await _itsModeFeature.GetStateAsync();
        if (currentState == itsMode)
        {
            await _windowsPowerPlanController.SetPowerPlanAsync(itsMode, true);
        }
    }

    private async Task ITSBalanceOverlayChangedAsync(ITSMode itsMode, WindowsPowerMode selectedMode, bool isAc)
    {
        if (IsRefreshing)
        {
            return;
        }

        if (isAc)
        {
            _settings.Store.ITSOverrides.SetPowerPlanBalanceOnAc(itsMode, selectedMode);
        }
        else
        {
            _settings.Store.ITSOverrides.SetPowerPlanBalanceOnDc(itsMode, selectedMode);
        }

        _settings.SynchronizeStore();

        var currentState = await _itsModeFeature.GetStateAsync();
        if (currentState == itsMode)
        {
            await _windowsPowerPlanController.SetPowerPlanAsync(itsMode, true);
        }
    }

    private async Task GodModePresetPowerPlanChangedAsync(string presetKey, WindowsPowerPlan windowsPowerPlan)
    {
        if (IsRefreshing)
        {
            return;
        }

        var presetKvp = _godModeSettings.Store.Presets.FirstOrDefault(profile => profile.Key.ToString() == presetKey);

        if (!presetKvp.Equals(default(KeyValuePair<Guid, GodModeSettingsStore.Preset>)) && presetKvp.Value != null)
        {
            var newOv = new Dictionary<PowerOverrideKey, string>(presetKvp.Value.Overrides ?? []);
            newOv.SetGuid(PowerOverrideKey.PowerPlan, windowsPowerPlan.Guid);
            if (windowsPowerPlan.Guid != Guid.Empty && !IsBalancedPlan(windowsPowerPlan.Guid))
            {
                newOv.Remove(PowerOverrideKey.PowerPlanBalanceOnAc);
                newOv.Remove(PowerOverrideKey.PowerPlanBalanceOnDc);
            }
            var updated = presetKvp.Value with { Overrides = newOv };
            _godModeSettings.Store.Presets[presetKvp.Key] = updated;
            _godModeSettings.SynchronizeStore();

            var currentState = await _powerModeFeature.GetStateAsync();
            if (currentState == PowerModeState.GodMode && _godModeSettings.Store.ActivePresetId.ToString() == presetKey)
            {
                await WindowsPowerPlanChangedAsync(windowsPowerPlan, PowerModeState.GodMode, updated);
            }
        }
    }

    private async Task GodModePresetBalanceOverlayChangedAsync(string presetKey, WindowsPowerMode selectedMode, bool isAc)
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
                newOv.SetEnum(PowerOverrideKey.PowerPlanBalanceOnAc, (WindowsPowerMode?)selectedMode);
            }
            else
            {
                newOv.SetEnum(PowerOverrideKey.PowerPlanBalanceOnDc, (WindowsPowerMode?)selectedMode);
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
