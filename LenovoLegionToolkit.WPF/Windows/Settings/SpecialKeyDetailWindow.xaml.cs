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

    private readonly SpecialKey _key;
    private bool _isRefreshing;

    private List<Guid> ActionList =>
        _settings.Store.KeyActions.GetValueOrDefault((int)_key, []);

    public SpecialKeyDetailWindow(SpecialKey key)
    {
        _key = key;

        InitializeComponent();

        Title = _title.Text = string.Format(Resource.SelectSpecialKeyPipelinesWindow_Configure_Title, GetSpecialKeyDisplayName(key));

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

        var mode = _settings.Store.KeyModes.TryGetValue((int)_key, out var m) ? m : CustomSpecialKey.Default;

        _modeComboBox.SetItems(
            [CustomSpecialKey.Default, CustomSpecialKey.Custom],
            mode,
            v => v.GetDisplayName());

        var isCustom = mode == CustomSpecialKey.Custom;
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

    private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing) return;

        if (!_modeComboBox.TryGetSelectedItem(out CustomSpecialKey mode)) return;

        _isRefreshing = true;

        _settings.Store.KeyModes[(int)_key] = mode;
        if (mode == CustomSpecialKey.Default)
            _settings.Store.KeyActions.Remove((int)_key);
        else if (!_settings.Store.KeyActions.ContainsKey((int)_key))
            _settings.Store.KeyActions[(int)_key] = [];
        _settings.SynchronizeStore();

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
        var mode = _settings.Store.KeyModes.TryGetValue((int)_key, out var m)
            ? m : CustomSpecialKey.Default;

        if (mode == CustomSpecialKey.Custom)
        {
            var selectedPipelines = _list.Items.OfType<PipelineListItem>()
                .Where(li => li.IsChecked)
                .Select(li => li.Pipeline.Id)
                .ToList();

            _settings.Store.KeyActions[(int)_key] = selectedPipelines;
        }

        _settings.SynchronizeStore();
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
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

    private string GetSpecialKeyDisplayName(SpecialKey key)
    {
        string str = key.ToString();
        if (str.StartsWith("Fn", StringComparison.OrdinalIgnoreCase) && str.Length > 2)
        {
            return string.Concat("Fn ", str.AsSpan(2));
        }
        return str;
    }
}
