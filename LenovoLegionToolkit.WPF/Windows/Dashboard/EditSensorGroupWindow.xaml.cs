using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.WPF.Controls;
using LenovoLegionToolkit.WPF.Converters;
using LenovoLegionToolkit.WPF.Settings;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace LenovoLegionToolkit.WPF.Windows.Dashboard;

public partial class EditSensorGroupWindow : BaseWindow
{
    private readonly SensorsControlSettings _settings = IoCContainer.Resolve<SensorsControlSettings>();
    public event EventHandler? Apply;

    public EditSensorGroupWindow()
    {
        InitializeComponent();
        this.DataContext = _settings.Store;
        IsVisibleChanged += EditSensorGroupWindow_IsVisibleChanged;
    }

    private async void EditSensorGroupWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
            await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _loader.IsLoading = true;
        _applyRevertStackPanel.Visibility = Visibility.Hidden;

        var loadingTask = Task.Delay(TimeSpan.FromMilliseconds(500));

        LoadSensors(false);

        await loadingTask;

        _applyRevertStackPanel.Visibility = Visibility.Visible;
        _loader.IsLoading = false;
    }

    private void LoadSensors(bool isDefault)
    {
        _groupsStackPanel.Children.Clear();
        if (!isDefault)
        {
            if (_settings.Store.VisibleItems == null || !_settings.Store.VisibleItems.Any())
            {
                var defaultItems = SensorGroup.DefaultGroups.SelectMany(group => group.Items).ToArray();
                _settings.Store.VisibleItems = defaultItems;
                _settings.SynchronizeStore();
            }

            foreach (var item in _settings.Store.VisibleItems)
            {
                var card = new Wpf.Ui.Controls.CardControl { Margin = new Thickness(0, 0, 16, 16) };
                var header = new CardHeaderControl { Title = EnumToLocalizedStringConverter.Convert(item) };
                card.Header = header;
                var button = new Wpf.Ui.Controls.Button
                {
                    Content = "Hide",
                    Tag = item
                };
                button.Click += RemoveButton_Click;
                card.Content = button;
                _groupsStackPanel.Children.Add(card);
            }
        }
        else
        {
            var defaultItems = SensorGroup.DefaultGroups.SelectMany(group => group.Items).ToArray();
            _settings.Store.VisibleItems = defaultItems;
            _settings.SynchronizeStore();
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var allSensors = Enum.GetValues(typeof(SensorItem)).Cast<SensorItem>().ToList();
        var currentSensors = (_settings.Store.VisibleItems ?? Array.Empty<SensorItem>()).ToList();
        var availableSensors = allSensors.Except(currentSensors).ToList();

        if (availableSensors.Any())
        {
            var selectedItem = availableSensors.First();
            var newItems = currentSensors.ToList();
            newItems.Add(selectedItem);
            _settings.Store.VisibleItems = newItems.ToArray();
            LoadSensors(false);
            _applyRevertStackPanel.Visibility = Visibility.Visible;
            _settings.SynchronizeStore();
        }
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.Button button && button.Tag is SensorItem itemToRemove)
        {
            var newItems = (_settings.Store.VisibleItems ?? Array.Empty<SensorItem>()).ToList();
            newItems.Remove(itemToRemove);
            _settings.Store.VisibleItems = newItems.ToArray();
            LoadSensors(false);
            _applyRevertStackPanel.Visibility = Visibility.Visible;
            _settings.SynchronizeStore();
        }
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        _applyRevertStackPanel.Visibility = Visibility.Collapsed;
        _settings.SynchronizeStore();
        Close();

        Apply?.Invoke(this, EventArgs.Empty);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DefaultButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.Reset();
        LoadSensors(true);
        LoadSensors(false);

        _applyRevertStackPanel.Visibility = Visibility.Visible;
        _settings.SynchronizeStore();
    }
}
