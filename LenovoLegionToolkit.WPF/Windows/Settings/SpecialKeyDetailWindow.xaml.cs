using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation;
using LenovoLegionToolkit.Lib.Automation.Pipeline;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

public partial class SpecialKeyDetailWindow
{
    private readonly AutomationProcessor _automationProcessor = IoCContainer.Resolve<AutomationProcessor>();
    private readonly SpecialKeySettings _settings = IoCContainer.Resolve<SpecialKeySettings>();

    private readonly int _keyCode;
    private readonly string _displayName;
    private bool _isRefreshing;
    private bool _descriptionDirty;
    private CustomSpecialKey _pendingMode;

    private List<Guid> ActionList =>
        _settings.Store.KeyActions.GetValueOrDefault(_keyCode, []);

    public SpecialKeyDetailWindow(SpecialKey key)
        : this((int)key, FormatDefaultDisplayName(key)) { }

    public SpecialKeyDetailWindow(int keyCode, string displayName)
    {
        _keyCode = keyCode;
        _displayName = displayName;

        InitializeComponent();

        Title = _title.Text = string.Format(Resource.SelectSpecialKeyPipelinesWindow_Configure_Title, displayName);

        IsVisibleChanged += SpecialKeyDetailWindow_IsVisibleChanged;
    }

    private async void SpecialKeyDetailWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
            await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _isRefreshing = true;

        _descriptionTextBox.Text = _settings.Store.KeyDescriptions.TryGetValue(_keyCode, out var desc)
            ? desc : "";
        _descriptionDirty = false;

        var isDriverKey = _keyCode >= SpecialKeySettings.SpecialKeySettingsStore.DriverKeyCodeOffset
            && Enum.IsDefined(typeof(DriverKey), (DriverKey)(_keyCode - SpecialKeySettings.SpecialKeySettingsStore.DriverKeyCodeOffset));
        var isBuiltIn = Enum.IsDefined(typeof(SpecialKey), (SpecialKey)_keyCode) || isDriverKey;
        _deleteButton.Visibility = isBuiltIn ? Visibility.Collapsed : Visibility.Visible;

        _pendingMode = _settings.Store.KeyModes.TryGetValue(_keyCode, out var m) ? m : CustomSpecialKey.Default;

        _modeComboBox.SetItems(
            [CustomSpecialKey.Default, CustomSpecialKey.Custom],
            _pendingMode,
            v => v.GetDisplayName());

        _modeHeader.Subtitle = Resource.SpecialKeyDetail_Mode_Description;

        var isCustom = _pendingMode == CustomSpecialKey.Custom;
        _customPanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        _showThisAppToggle.IsChecked = isCustom && ActionList.Count == 0;

        if (isCustom)
            await RefreshPipelineListAsync();

        _isRefreshing = false;
    }

    private async Task RefreshPipelineListAsync()
    {
        _loader.IsLoading = true;

        var allPipelines = await _automationProcessor.GetPipelinesAsync();
        var pipelines = allPipelines.Where(p => p.Trigger is null).OrderBy(p => p.Name).ToArray();

        _list.Items.Clear();

        if (pipelines.IsEmpty())
            _list.Items.Add(Resource.SelectSpecialKeyPipelinesWindow_List_Empty);

        foreach (var pipeline in pipelines)
        {
            var item = new PipelineListItem(pipeline)
            {
                IsChecked = ActionList.Contains(pipeline.Id)
            };
            _list.Items.Add(item);
        }

        EnableListIfPossible();

        _loader.IsLoading = false;
    }

    private void DescriptionTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isRefreshing) return;
        _descriptionDirty = true;
    }

    private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing) return;

        if (!_modeComboBox.TryGetSelectedItem(out CustomSpecialKey mode)) return;

        _isRefreshing = true;

        _pendingMode = mode;

        var isCustom = mode == CustomSpecialKey.Custom;
        _customPanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        _showThisAppToggle.IsChecked = isCustom && ActionList.Count == 0;

        if (isCustom)
            _ = RefreshPipelineListAsync();

        _isRefreshing = false;
    }

    private void ShowThisAppToggle_Click(object sender, RoutedEventArgs e) => EnableListIfPossible();

    private void EnableListIfPossible() => _list.IsEnabled = !(_showThisAppToggle.IsChecked ?? false);

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_descriptionDirty)
        {
            if (string.IsNullOrWhiteSpace(_descriptionTextBox.Text))
                _settings.Store.KeyDescriptions.Remove(_keyCode);
            else
                _settings.Store.KeyDescriptions[_keyCode] = _descriptionTextBox.Text.Trim();
        }

        _settings.Store.KeyModes[_keyCode] = _pendingMode;

        if (_pendingMode == CustomSpecialKey.Default)
        {
            _settings.Store.KeyActions.Remove(_keyCode);
        }
        else
        {
            var selectedPipelines = _list.Items.OfType<PipelineListItem>()
                .Where(li => li.IsChecked)
                .Select(li => li.Pipeline.Id)
                .ToList();

            _settings.Store.KeyActions[_keyCode] = selectedPipelines;
        }

        _settings.SynchronizeStore();
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.Store.KeyDescriptions.Remove(_keyCode);
        _settings.Store.KeyModes.Remove(_keyCode);
        _settings.Store.KeyActions.Remove(_keyCode);
        _settings.SynchronizeStore();
        Close();
    }

    private static string FormatDefaultDisplayName(SpecialKey key)
    {
        string str = key.ToString();
        if (str.StartsWith("Fn", StringComparison.OrdinalIgnoreCase) && str.Length > 2)
            return string.Concat("Fn + ", str.AsSpan(2));
        return str;
    }

    private class PipelineListItem : UserControl
    {
        private readonly Grid _grid = new()
        {
            Margin = new(8, 4, 0, 16),
            ColumnDefinitions =
            {
                new() { Width = new(32, GridUnitType.Pixel) },
                new() { Width = new(1, GridUnitType.Star) },
            },
        };

        private readonly CheckBox _checkBox = new();

        private readonly TextBlock _nameTextBox = new()
        {
            TextAlignment = TextAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };

        public AutomationPipeline Pipeline { get; }

        public bool IsChecked
        {
            get => _checkBox.IsChecked ?? false;
            set => _checkBox.IsChecked = value;
        }

        public PipelineListItem(AutomationPipeline pipeline)
        {
            Pipeline = pipeline;

            _nameTextBox.Text = Pipeline.Name;

            Grid.SetColumn(_checkBox, 0);
            Grid.SetColumn(_nameTextBox, 1);

            _grid.Children.Add(_checkBox);
            _grid.Children.Add(_nameTextBox);

            System.Windows.Automation.AutomationProperties.SetLabeledBy(_checkBox, _nameTextBox);

            Content = _grid;
        }
    }
}
