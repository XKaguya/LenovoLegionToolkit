using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.WPF.Controls.KeyboardBacklight.RGB;
using LenovoLegionToolkit.WPF.Controls.KeyboardBacklight.Spectrum;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Windows.Utils;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

namespace LenovoLegionToolkit.WPF.Pages;

public partial class KeyboardBacklightPage
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();

    public KeyboardBacklightPage() => InitializeComponent();

    private async void KeyboardBacklightPage_Initialized(object? sender, EventArgs e)
    {
        _titleTextBlock.Visibility = Visibility.Collapsed;

        await Task.Delay(TimeSpan.FromSeconds(1));

        _titleTextBlock.Visibility = Visibility.Visible;

        string registryPath = @"SOFTWARE\Microsoft\Lighting";
        string valueName = "AmbientLightingEnabled";

        if (!_settings.Store.DynamicLightingWarningDontShowAgain)
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(registryPath);
            if (key != null)
            {
                object? value = key.GetValue(valueName);
                if (value != null)
                {
                    if (value is int intValue)
                    {
                        if (intValue == 1)
                        {
                            var dialog = new DialogWindow
                            {
                                Title = Resource.Warning,
                                Content = Resource.KeyboardBacklightPage_DynamicLightingEnabled,
                                Owner = App.Current.MainWindow
                            };

                            dialog.DontShowAgainCheckBox.Visibility = Visibility.Visible;
                            dialog.ShowDialog();

                            var result = dialog.Result.Item1;
                            var dontShowAgain = dialog.Result.Item2;

                            if (result)
                            {
                                var processStartInfo = new ProcessStartInfo
                                {
                                    FileName = "reg.exe",
                                    Arguments = @"add ""HKEY_CURRENT_USER\SOFTWARE\Microsoft\Lighting"" /v AmbientLightingEnabled /t REG_DWORD /d 0 /f",
                                    WindowStyle = ProcessWindowStyle.Hidden,
                                    CreateNoWindow = true
                                };

                                Process.Start(processStartInfo)?.WaitForExit();
                            }

                            if (dontShowAgain)
                            {
                                _settings.Store.DynamicLightingWarningDontShowAgain = true;
                                _settings.SynchronizeStore();
                            }
                        }
                    }
                }
            }
        }

        var spectrumController = IoCContainer.Resolve<SpectrumKeyboardBacklightController>();
        if (await spectrumController.IsSupportedAsync())
        {
            var control = new SpectrumKeyboardBacklightControl();
            _content.Children.Add(control);
            _loader.IsLoading = false;
            return;
        }

        var rgbController = IoCContainer.Resolve<RGBKeyboardBacklightController>();
        if (await rgbController.IsSupportedAsync())
        {
            var control = new RGBKeyboardBacklightControl();
            _content.Children.Add(control);
            _loader.IsLoading = false;
            return;
        }

        _noKeyboardsText.Visibility = Visibility.Visible;
        _loader.IsLoading = false;
    }

    public static async Task<bool> IsSupportedAsync()
    {
        var spectrumController = IoCContainer.Resolve<SpectrumKeyboardBacklightController>();
        if (await spectrumController.IsSupportedAsync())
            return true;

        var rgbController = IoCContainer.Resolve<RGBKeyboardBacklightController>();
        if (await rgbController.IsSupportedAsync())
            return true;

        return false;
    }
}
