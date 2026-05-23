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
    private static readonly string[] _symbol24Names = Enum.GetNames<SymbolRegular>()
        .Where(s => s.EndsWith("24", StringComparison.OrdinalIgnoreCase))
        .OrderBy(s => s)
        .ToArray();

    private readonly ThrottleLastDispatcher _throttleDispatcher = new(TimeSpan.FromMilliseconds(300));

    public event EventHandler? SymbolChanged;

    private SymbolRegular? _selectedSymbol;

    private string[] _filteredSymbolNames = [];
    private int _loadedCount = 0;
    private const int BatchSize = 60;

    public SymbolRegular? SelectedSymbol
    {
        get => _selectedSymbol;
        set
        {
            _selectedSymbol = value;
            _symbolIcon.Symbol = value ?? SymbolRegular.Empty;
        }
    }

    private SymbolRegular? _overlaySymbol;

    public SymbolRegular? OverlaySymbol
    {
        get => _overlaySymbol;
        set
        {
            _overlaySymbol = value;
            if (value.HasValue)
            {
                _overlayIcon.Symbol = value.Value;
                _overlayIcon.Visibility = Visibility.Visible;
            }
            else
            {
                _overlayIcon.Symbol = SymbolRegular.Empty;
                _overlayIcon.Visibility = Visibility.Collapsed;
            }
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
            Dispatcher.InvokeAsync(Refresh);
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
        _scrollViewer.ScrollToTop();

        _filteredSymbolNames = _symbol24Names
            .Where(s => s.Contains(_filterTextBox.Text, StringComparison.CurrentCultureIgnoreCase))
            .ToArray();

        _loadedCount = 0;
        LoadNextBatch();
    }

    private void LoadNextBatch()
    {
        if (_loadedCount >= _filteredSymbolNames.Length)
            return;

        var batch = _filteredSymbolNames.Skip(_loadedCount).Take(BatchSize);
        foreach (var item in batch)
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
        _loadedCount += BatchSize;
    }

    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 40)
        {
            LoadNextBatch();
        }
    }
}
