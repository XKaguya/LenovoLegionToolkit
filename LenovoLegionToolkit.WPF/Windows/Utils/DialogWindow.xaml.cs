using System.Windows;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public partial class DialogWindow
{
    public static readonly DependencyProperty DialogTitleProperty =
        DependencyProperty.Register("DialogTitle", typeof(string), typeof(DialogWindow));

    public static readonly DependencyProperty DialogMessageProperty =
        DependencyProperty.Register("DialogMessage", typeof(string), typeof(DialogWindow));

    public string DialogTitle
    {
        get => (string)GetValue(DialogTitleProperty);
        set => SetValue(DialogTitleProperty, value);
    }

    public string DialogMessage
    {
        get => (string)GetValue(DialogMessageProperty);
        set => SetValue(DialogMessageProperty, value);
    }

    public (bool, bool) Result { get; private set; }

    public DialogWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        Result = (true, DontShowAgainCheckBox.IsChecked!.Value);
        Close();
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        Result = (false, DontShowAgainCheckBox.IsChecked!.Value);
        Close();
    }

    private void DialogWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        Hide();
    }
}