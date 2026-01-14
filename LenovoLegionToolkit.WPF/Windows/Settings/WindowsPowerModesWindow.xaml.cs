using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.WPF.Controls;
using LenovoLegionToolkit.WPF.Controls.Custom;
using LenovoLegionToolkit.WPF.Extensions;
using static LenovoLegionToolkit.Lib.Settings.GodModeSettings;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

public partial class WindowsPowerModesWindow
{
    private readonly PowerModeFeature _powerModeFeature = IoCContainer.Resolve<PowerModeFeature>();
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
            await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _loader.IsLoading = true;

        var loadingTask = Task.Delay(500);

        var powerModes = Enum.GetValues<WindowsPowerMode>();
        Refresh(_quietModeComboBox, powerModes, PowerModeState.Quiet);
        Refresh(_balanceModeComboBox, powerModes, PowerModeState.Balance);
        Refresh(_performanceModeComboBox, powerModes, PowerModeState.Performance);

        var allStates = await _powerModeFeature.GetAllStatesAsync();
        if (allStates.Contains(PowerModeState.Extreme))
            Refresh(_extremeModeComboBox, powerModes, PowerModeState.Extreme);
        else
            _extremeModeComboBox.Visibility = Visibility.Collapsed;

        if (allStates.Contains(PowerModeState.GodMode))
        {
            _godModeCardControl.Visibility = Visibility.Visible;

            var controller = await _godModeController.GetControllerAsync().ConfigureAwait(false);
            var presets = await controller.GetGodModePresetsAsync().ConfigureAwait(false);

            // Presets not only one, Create combo boxes.
            if (presets.Count > 1)
            {
                _godModeComboBox.Visibility = Visibility.Collapsed;
                _godModePresetsContainer.Visibility = Visibility.Visible;
                _godModePresetsContainer.Children.Clear();

                foreach (var preset in presets)
                {
                    var cardControl = new CardControl
                    {
                        Margin = new Thickness(0, 8, 0, 8)
                    };

                    var headerControl = new CardHeaderControl
                    {
                        Title = preset.Value.Name
                    };
                    cardControl.Header = headerControl;

                    var comboBox = new ComboBox
                    {
                        MinWidth = 300,
                        Margin = new Thickness(0, 8, 0, 8),
                        Tag = preset.Key,
                        MaxDropDownHeight = 300
                    };

                    // Get God Mode power mode.
                    var currentPowerMode = GetGodModePresetPowerMode(preset.Key.ToString());
                    var effectivePowerMode = currentPowerMode ?? _settings.Store.PowerModes.GetValueOrDefault(PowerModeState.GodMode, WindowsPowerMode.Balanced);

                    comboBox.SetItems(powerModes, effectivePowerMode, pm => pm.GetDisplayName());

                    comboBox.SelectionChanged += async (s, e) =>
                    {
                        if (comboBox.TryGetSelectedItem(out WindowsPowerMode selectedMode))
                        {
                            await GodModePresetPowerModeChangedAsync(preset.Key.ToString(), selectedMode);
                        }
                    };

                    cardControl.Content = comboBox;
                    _godModePresetsContainer.Children.Add(cardControl);
                }
            }
            else
            {
                // If only one preset, using default style.
                _godModePresetsContainer.Visibility = Visibility.Collapsed;
                _godModeComboBox.Visibility = Visibility.Visible;
                Refresh(_godModeComboBox, powerModes, PowerModeState.GodMode);
            }
        }
        else
        {
            _godModeCardControl.Visibility = Visibility.Collapsed;
        }

        await loadingTask;

        _loader.IsLoading = false;
    }

    private void Refresh(ComboBox comboBox, WindowsPowerMode[] windowsPowerModes, PowerModeState powerModeState)
    {
        var selectedValue = _settings.Store.PowerModes.GetValueOrDefault(powerModeState, WindowsPowerMode.Balanced);
        comboBox.SetItems(windowsPowerModes, selectedValue, pm => pm.GetDisplayName());
    }

    private WindowsPowerMode? GetGodModePresetPowerMode(string presetKey)
    {
        if (Guid.TryParse(presetKey, out var presetGuid) &&
            _godModeSettings.Store.Presets.TryGetValue(presetGuid, out var preset))
        {
            // Return PowerMode if was set.
            if (preset.PowerMode.HasValue)
            {
                return preset.PowerMode.Value;
            }
        }

        // Return God Mode selected.
        return _settings.Store.PowerModes.GetValueOrDefault(PowerModeState.GodMode, WindowsPowerMode.Balanced);
    }

    private async Task WindowsPowerModeChangedAsync(WindowsPowerMode windowsPowerMode, PowerModeState powerModeState, GodModeSettingsStore.Preset? preset = null)
    {
        if (IsRefreshing)
            return;

        if (preset == null)
        {
            _settings.Store.PowerModes[powerModeState] = windowsPowerMode;
        }
        // If Preset argument was passed, update the preset and settings
        else
        {
            var powerMode = preset.PowerMode;
            if (!powerMode.HasValue)
            {
                return;
            }

            _settings.Store.PowerModes[powerModeState] = powerMode.Value;
        }

        _settings.SynchronizeStore();

        await _powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync();
    }

    private async Task GodModePresetPowerModeChangedAsync(string presetKey, WindowsPowerMode windowsPowerMode)
    {
        if (IsRefreshing)
            return;

        var presetKvp = _godModeSettings.Store.Presets.FirstOrDefault(profile => profile.Key.ToString() == presetKey);

        if (!presetKvp.Equals(default(KeyValuePair<Guid, GodModeSettingsStore.Preset>)) && presetKvp.Value != null)
        {
            var preset = presetKvp.Value;
            var presetGuid = presetKvp.Key;

            var updatedPreset = new GodModeSettingsStore.Preset()
            {
                Name = preset.Name,
                PowerMode = windowsPowerMode,
                PowerPlanGuid = preset.PowerPlanGuid,
                CPULongTermPowerLimit = preset.CPULongTermPowerLimit,
                CPUShortTermPowerLimit = preset.CPUShortTermPowerLimit,
                CPUPeakPowerLimit = preset.CPUPeakPowerLimit,
                CPUCrossLoadingPowerLimit = preset.CPUCrossLoadingPowerLimit,
                CPUPL1Tau = preset.CPUPL1Tau,
                APUsPPTPowerLimit = preset.APUsPPTPowerLimit,
                CPUTemperatureLimit = preset.CPUTemperatureLimit,
                GPUPowerBoost = preset.GPUPowerBoost,
                GPUConfigurableTGP = preset.GPUConfigurableTGP,
                GPUTemperatureLimit = preset.GPUTemperatureLimit,
                GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline = preset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline,
                GPUToCPUDynamicBoost = preset.GPUToCPUDynamicBoost,
                FanTable = preset.FanTable,
                FanFullSpeed = preset.FanFullSpeed,
                MinValueOffset = preset.MinValueOffset,
                MaxValueOffset = preset.MaxValueOffset,
                PrecisionBoostOverdriveScaler = preset.PrecisionBoostOverdriveScaler,
                PrecisionBoostOverdriveBoostFrequency = preset.PrecisionBoostOverdriveBoostFrequency,
                AllCoreCurveOptimizer = preset.AllCoreCurveOptimizer,
                EnableAllCoreCurveOptimizer = preset.EnableAllCoreCurveOptimizer,
                EnableOverclocking = preset.EnableOverclocking,
            };

            _godModeSettings.Store.Presets[presetGuid] = updatedPreset;
            _godModeSettings.SynchronizeStore();

            await WindowsPowerModeChangedAsync(windowsPowerMode, PowerModeState.GodMode, updatedPreset);
        }
    }

    private async void QuietModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_quietModeComboBox.TryGetSelectedItem(out WindowsPowerMode windowsPowerMode))
            await WindowsPowerModeChangedAsync(windowsPowerMode, PowerModeState.Quiet);
    }

    private async void BalanceModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_balanceModeComboBox.TryGetSelectedItem(out WindowsPowerMode windowsPowerMode))
            await WindowsPowerModeChangedAsync(windowsPowerMode, PowerModeState.Balance);
    }

    private async void PerformanceModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_performanceModeComboBox.TryGetSelectedItem(out WindowsPowerMode windowsPowerMode))
            await WindowsPowerModeChangedAsync(windowsPowerMode, PowerModeState.Performance);
    }

    private async void ExtremeModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_extremeModeComboBox.TryGetSelectedItem(out WindowsPowerMode windowsPowerMode))
            await WindowsPowerModeChangedAsync(windowsPowerMode, PowerModeState.Extreme);
    }

    private async void GodModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_godModeComboBox.TryGetSelectedItem(out WindowsPowerMode windowsPowerMode))
            await WindowsPowerModeChangedAsync(windowsPowerMode, PowerModeState.GodMode);
    }
}