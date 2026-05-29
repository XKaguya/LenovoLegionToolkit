using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

public partial class ExcludeProcessesWindow
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();

    public ExcludeProcessesWindow()
    {
        InitializeComponent();
        IsVisibleChanged += ExcludeProcessesWindow_IsVisibleChanged;
    }

    private async void ExcludeProcessesWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
            await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _loader.IsLoading = true;
        var loadingTask = Task.Delay(200);

        _list.Items.Clear();

        foreach (var process in _settings.Store.ExcludedProcesses.OrderBy(x => x))
        {
            AddProcessToList(process);
        }

        await loadingTask;
        _loader.IsLoading = false;
        _inputBox.Focus();
    }

    private void AddProcessToList(string processName)
    {
        var item = new ListItem(processName);
        item.RemoveRequested += (s, e) => _list.Items.Remove(item);
        _list.Items.Add(item);
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        SubmitInput();
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SubmitInput();
        }
    }

    private void SubmitInput()
    {
        var text = _inputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        foreach (ListItem item in _list.Items)
        {
            if (item.ProcessName.Equals(text, StringComparison.OrdinalIgnoreCase))
            {
                _inputBox.Text = string.Empty;
                return;
            }
        }

        AddProcessToList(text);
        _inputBox.Text = string.Empty;
        _inputBox.Focus();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var processes = _list.Items.OfType<ListItem>().Select(x => x.ProcessName).ToList();

        _settings.Store.ExcludedProcesses = processes;
        _settings.SynchronizeStore();

        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private class ListItem : UserControl
    {
        public event EventHandler? RemoveRequested;
        public string ProcessName { get; }

        public ListItem(string processName)
        {
            ProcessName = processName;
            
            var grid = new Grid
            {
                Margin = new Thickness(8, 4, 0, 16),
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto }
                }
            };

            var textBlock = new TextBlock
            {
                Text = processName,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 14
            };

            var removeButton = new Button
            {
                Icon = SymbolRegular.Dismiss24,
                ToolTip = Resource.Delete,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(8, 2, 8, 2),
                Appearance = ControlAppearance.Transparent
            };
            removeButton.Click += (s, e) => RemoveRequested?.Invoke(this, EventArgs.Empty);

            Grid.SetColumn(textBlock, 0);
            Grid.SetColumn(removeButton, 1);

            grid.Children.Add(textBlock);
            grid.Children.Add(removeButton);

            Content = grid;
        }
    }
}
