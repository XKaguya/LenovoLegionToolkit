using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
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
            await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _loader.IsLoading = true;

        var loadingTask = Task.Delay(500);

        var compatibility = await Compatibility.GetMachineInformationAsync();
        _aoAcWarningCard.Visibility = compatibility.Properties.SupportsAlwaysOnAc.status
            ? Visibility.Visible
            : Visibility.Collapsed;

        var powerPlans = _windowsPowerPlanController.GetPowerPlans().OrderBy(x => x.Name).Prepend(DefaultValue).ToArray();
        Refresh(_quietModeComboBox, powerPlans, PowerModeState.Quiet);
        Refresh(_balanceModeComboBox, powerPlans, PowerModeState.Balance);
        Refresh(_performanceModeComboBox, powerPlans, PowerModeState.Performance);

        var allStates = await _powerModeFeature.GetAllStatesAsync();
        if (allStates.Contains(PowerModeState.Extreme))
            Refresh(_extremeModeComboBox, powerPlans, PowerModeState.Extreme);
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

                    // Get God Mode power plan.
                    var currentPowerPlanGuid = GetGodModePresetPowerPlan(preset.Key.ToString());
                    WindowsPowerPlan selectedPowerPlan;

                    if (currentPowerPlanGuid.HasValue)
                    {
                        selectedPowerPlan = powerPlans.FirstOrDefault(pp => pp.Guid == currentPowerPlanGuid.Value);
                    }
                    else
                    {
                        var globalPowerPlanGuid = _settings.Store.PowerPlans.GetValueOrDefault(PowerModeState.GodMode);
                        selectedPowerPlan = powerPlans.FirstOrDefault(pp => pp.Guid == globalPowerPlanGuid);
                    }

                    var effectivePowerPlan = (selectedPowerPlan == default(WindowsPowerPlan)) ? DefaultValue : selectedPowerPlan;

                    comboBox.SetItems(powerPlans, effectivePowerPlan, pp => pp.Name);

                    comboBox.SelectionChanged += async (s, e) =>
                    {
                        if (comboBox.TryGetSelectedItem(out WindowsPowerPlan selectedPlan))
                        {
                            await GodModePresetPowerPlanChangedAsync(preset.Key.ToString(), selectedPlan);
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
                Refresh(_godModeComboBox, powerPlans, PowerModeState.GodMode);
            }
        }
        else
        {
            _godModeCardControl.Visibility = Visibility.Collapsed;
        }

        await loadingTask;
        _loader.IsLoading = false;
    }

    private void Refresh(ComboBox comboBox, WindowsPowerPlan[] windowsPowerPlans, PowerModeState powerModeState)
    {
        var settingsPowerPlanGuid = _settings.Store.PowerPlans.GetValueOrDefault(powerModeState);
        var selectedValue = windowsPowerPlans.FirstOrDefault(pp => pp.Guid == settingsPowerPlanGuid);
        var effectiveValue = (selectedValue == default(WindowsPowerPlan)) ? DefaultValue : selectedValue;
        comboBox.SetItems(windowsPowerPlans, effectiveValue, pp => pp.Name);
    }

    private Guid? GetGodModePresetPowerPlan(string presetKey)
    {
        if (Guid.TryParse(presetKey, out var presetGuid) &&
            _godModeSettings.Store.Presets.TryGetValue(presetGuid, out var preset))
        {
            // Return PowerPlanGuid if was set.
            if (preset.PowerPlanGuid != null && preset.PowerPlanGuid != Guid.Empty)
            {
                return preset.PowerPlanGuid;
            }
        }

        // Return God Mode selected.
        return _settings.Store.PowerPlans.GetValueOrDefault(PowerModeState.GodMode);
    }

    private async Task WindowsPowerPlanChangedAsync(WindowsPowerPlan windowsPowerPlan, PowerModeState powerModeState, GodModeSettingsStore.Preset? preset = null)
    {
        if (IsRefreshing)
            return;

        if (preset == null)
        {
            _settings.Store.PowerPlans[powerModeState] = windowsPowerPlan.Guid;
        }
        // If Preset argument was passed, Call EnsureCorrectWindowsPowerSettingsAreSetAsync(preset) to ensure power plan.
        else
        {
            var powerPlan = preset.PowerPlanGuid;
            if (powerPlan == null)
            {
                return;
            }

            _settings.Store.PowerPlans[powerModeState] = powerPlan.Value;
        }

        _settings.SynchronizeStore();

        await _powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync(preset);
    }

    private async Task GodModePresetPowerPlanChangedAsync(string presetKey, WindowsPowerPlan windowsPowerPlan)
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
                PowerPlanGuid = windowsPowerPlan.Guid,
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

            await WindowsPowerPlanChangedAsync(windowsPowerPlan, PowerModeState.GodMode, updatedPreset);
        }
    }

    private async void QuietModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_quietModeComboBox.TryGetSelectedItem(out WindowsPowerPlan windowsPowerPlan))
            await WindowsPowerPlanChangedAsync(windowsPowerPlan, PowerModeState.Quiet);
    }

    private async void BalanceModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_balanceModeComboBox.TryGetSelectedItem(out WindowsPowerPlan windowsPowerPlan))
            await WindowsPowerPlanChangedAsync(windowsPowerPlan, PowerModeState.Balance);
    }

    private async void PerformanceModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_performanceModeComboBox.TryGetSelectedItem(out WindowsPowerPlan windowsPowerPlan))
            await WindowsPowerPlanChangedAsync(windowsPowerPlan, PowerModeState.Performance);
    }

    private async void ExtremeModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_extremeModeComboBox.TryGetSelectedItem(out WindowsPowerPlan windowsPowerPlan))
            await WindowsPowerPlanChangedAsync(windowsPowerPlan, PowerModeState.Extreme);
    }

    private async void GodModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_godModeComboBox.TryGetSelectedItem(out WindowsPowerPlan windowsPowerPlan))
            await WindowsPowerPlanChangedAsync(windowsPowerPlan, PowerModeState.GodMode);
    }
}