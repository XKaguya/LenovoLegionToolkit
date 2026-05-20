using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib.Utils;
using Wpf.Ui.Common;
using Button = Wpf.Ui.Controls.Button;

namespace LenovoLegionToolkit.WPF.Controls;

public partial class SymbolRegularPickerControl
{
    private readonly ThrottleLastDispatcher _throttleDispatcher = new(TimeSpan.FromMilliseconds(300));

    public event EventHandler? SymbolChanged;

    private SymbolRegular? _selectedSymbol;

    public SymbolRegular? SelectedSymbol
    {
        get => _selectedSymbol;
        set
        {
            _selectedSymbol = value;
            _button.Icon = value ?? SymbolRegular.Empty;
        }
    }

    public SymbolRegularPickerControl()
    {
        InitializeComponent();
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        if (!_popup.IsOpen)
        {
            Refresh();
            _popup.IsOpen = true;
        }
        e.Handled = true;
    }

    private async void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e) =>
        await _throttleDispatcher.DispatchAsync(() =>
        {
            Dispatcher.Invoke(Refresh);
            return Task.CompletedTask;
        });

    private void ItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        SelectedSymbol = button.Icon;
        _popup.IsOpen = false;
        SymbolChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DefaultButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedSymbol = null;
        _popup.IsOpen = false;
        SymbolChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Refresh()
    {
        _iconsPanel.Children.Clear();

        var items = Enum.GetNames<SymbolRegular>()
            .Where(s => s.EndsWith("24", StringComparison.CurrentCultureIgnoreCase))
            .Where(s => s.Contains(_filterTextBox.Text, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(s => s)
            .ToArray();

        foreach (var item in items)
        {
            var button = new Button
            {
                Icon = Enum.Parse<SymbolRegular>(item),
                FontSize = 24,
                Width = 48,
                Height = 48,
                Margin = new Thickness(2)
            };
            button.Click += ItemButton_Click;
            _iconsPanel.Children.Add(button);
        }
    }
}
