using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.Lib.Utils.Warranty;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public partial class DeviceInformationWindow
{
    private readonly WarrantyChecker _warrantyChecker = IoCContainer.Resolve<WarrantyChecker>();

    public DeviceInformationWindow()
    {
        InitializeComponent();

        PreviewKeyDown += (s, e) => {
            if (e.Key == Key.System && e.SystemKey == Key.LeftAlt)
            {
                e.Handled = true;
                Keyboard.ClearFocus();
            }
        };
    }

    private async void DeviceInformationWindow_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync(bool forceRefresh = false)
    {
        var mi = await Compatibility.GetMachineInformationAsync();

        _manufacturerLabel.Text = mi.Vendor;
        _modelLabel.Text = mi.Model;
        _mtmLabel.Text = mi.MachineType;
        _serialNumberLabel.Text = mi.SerialNumber;
        _biosLabel.Text = mi.BiosVersionRaw;

        try
        {
            _refreshWarrantyButton.IsEnabled = false;

            _warrantyStartLabel.Text = "-";
            _warrantyEndLabel.Text = "-";
            _warrantyLinkCardAction.Tag = null;
            _warrantyLinkCardAction.IsEnabled = false;

            var language = await LocalizationHelper.GetLanguageAsync(); 
            var warrantyInfo = await _warrantyChecker.GetWarrantyInfo(mi, language, forceRefresh);

            if (!warrantyInfo.HasValue)
                return;

            _warrantyStartLabel.Text = warrantyInfo.Value.Start is not null ? warrantyInfo.Value.Start?.ToString(LocalizationHelper.ShortDateFormat) : "-";
            _warrantyEndLabel.Text = warrantyInfo.Value.End is not null ? warrantyInfo.Value.End?.ToString(LocalizationHelper.ShortDateFormat) : "-";
            _warrantyLinkCardAction.Tag = warrantyInfo.Value.Link;
            _warrantyLinkCardAction.IsEnabled = true;
            _warrantyInfo.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't load warranty info.", ex);
        }
        finally
        {
            _refreshWarrantyButton.IsEnabled = true;
        }
    }

    private async void RefreshWarrantyButton_OnClick(object sender, RoutedEventArgs e) => await RefreshAsync(true);

    private async void DeviceCardControl_Click(object sender, RoutedEventArgs e)
    {
        if (((sender as CardControl)?.Content as TextBlock)?.Text is not { } str)
            return;

        try
        {
            Clipboard.SetText(str);
            await _snackBar.ShowAsync(Resource.CopiedToClipboard_Title, string.Format(Resource.CopiedToClipboard_Message_WithParam, str));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't copy to clipboard", ex);
        }
    }

    private void WarrantyLinkCardAction_OnClick(object sender, RoutedEventArgs e)
    {
        var link = _warrantyLinkCardAction.Tag as Uri;
        link?.Open();
    }
}
