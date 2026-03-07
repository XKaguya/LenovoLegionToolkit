using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.WPF.Settings;
using LenovoLegionToolkit.WPF.Utils;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

public partial class SensorSettingsWindow
{
    private readonly SensorsControlSettings _sensorsSettings = IoCContainer.Resolve<SensorsControlSettings>();

    public SensorSettingsWindow()
    {
        InitializeComponent();

        _cpuFrequencySelector.SelectedIndex = _sensorsSettings.Store.ShowCpuAverageFrequency ? 1 : 0;
        _memoryDisplayModeSelector.SelectedIndex = _sensorsSettings.Store.DisplayMemoryInGigabytes ? 1 : 0;

        if (Displays.HasMultipleGpus())
        {
            _gpuSelectionCard.Visibility = Visibility.Visible;
            _gpuSelector.SelectedIndex = _sensorsSettings.Store.SelectedGpuIsIgpu ? 1 : 0;
        }
        else
        {
            _gpuSelectionCard.Visibility = Visibility.Collapsed;
        }
    }

    private void DefaultButton_Click(object sender, RoutedEventArgs e)
    {
        _sensorsSettings.Store.ShowCpuAverageFrequency = false;
        _sensorsSettings.Store.SelectedGpuIsIgpu = false;
        _sensorsSettings.Store.DisplayMemoryInGigabytes = false;
        _cpuFrequencySelector.SelectedIndex = 0;
        _gpuSelector.SelectedIndex = 0;
        _memoryDisplayModeSelector.SelectedIndex = 0;
        _sensorsSettings.SynchronizeStore();

        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        _sensorsSettings.Store.ShowCpuAverageFrequency = _cpuFrequencySelector.SelectedIndex == 1;
        if (Displays.HasMultipleGpus())
        {
            _sensorsSettings.Store.SelectedGpuIsIgpu = _gpuSelector.SelectedIndex == 1;
        }

        _sensorsSettings.Store.DisplayMemoryInGigabytes = _memoryDisplayModeSelector.SelectedIndex == 1;

        _sensorsSettings.SynchronizeStore();
        Close();
    }
}
