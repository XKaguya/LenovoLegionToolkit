using System;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

public partial class KeyDiscoveryWindow
{
    private IDisposable? _keyDiscovery;

    public KeyDiscoveryWindow()
    {
        InitializeComponent();
        Title = _title.Text = Resource.SpecialKeyDetailWindow_KeyDiscovery;
        _discoveryButton.Content = Resource.SpecialKeyDetailWindow_KeyDiscovery_Start;
    }

    private void DiscoveryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_keyDiscovery is not null)
            StopKeyDiscovery();
        else
            StartKeyDiscovery();
    }

    private void StartKeyDiscovery()
    {
        _discoveryResult.Text = string.Empty;
        _discoveryButton.Content = Resource.SpecialKeyDetailWindow_KeyDiscovery_Stop;

        _keyDiscovery = WMI.LenovoUtilityEvent.Listen(value =>
        {
            var key = (SpecialKey)value;
            Dispatcher.Invoke(() =>
            {
                _discoveryResult.Text = $"Detected: {key} (code: {value})";
            });
        });
    }

    private void StopKeyDiscovery()
    {
        _keyDiscovery?.Dispose();
        _keyDiscovery = null;
        _discoveryButton.Content = Resource.SpecialKeyDetailWindow_KeyDiscovery_Start;
    }
}
