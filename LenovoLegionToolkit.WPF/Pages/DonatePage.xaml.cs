using System.Windows;
using LenovoLegionToolkit.WPF.Extensions;

namespace LenovoLegionToolkit.WPF.Pages;

public partial class DonatePage
{
    public DonatePage()
    {
        InitializeComponent();
    }

    private void BartoszPayPal_Click(object sender, RoutedEventArgs e)
    {
        Constants.BartoszPayPalUri.Open();
        e.Handled = true;
    }

    private void DrSkinnerGitHub_Click(object sender, RoutedEventArgs e)
    {
        Constants.DrSkinnerGitHubUri.Open();
        e.Handled = true;
    }
}
