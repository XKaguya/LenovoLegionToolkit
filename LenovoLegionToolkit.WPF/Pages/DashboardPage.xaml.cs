﻿using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Controls.Dashboard;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Settings;
using LenovoLegionToolkit.WPF.Windows.Dashboard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.WPF.Pages;

public partial class DashboardPage
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly DashboardSettings _dashboardSettings = IoCContainer.Resolve<DashboardSettings>();

    private readonly List<DashboardGroupControl> _dashboardGroupControls = [];
    private FrameworkElement sensorControl;

    public DashboardPage()
    {
        InitializeComponent();

        PreviewKeyDown += (s, e) => {
            if (e.Key == Key.System && e.SystemKey == Key.LeftAlt)
            {
                e.Handled = true;
                Keyboard.ClearFocus();
            }
        };

        if (!_settings.Store.UseNewSensorDashboard)
        {
            sensorControl = new SensorsControl();
        }
        else
        {
            sensorControl = new SensorsControlV2();
        }

        int contentIndex = _panel.Children.IndexOf(_content);
        sensorControl.Margin = new Thickness(0, 16, 16, 0);
        _panel.Children.Insert(contentIndex, sensorControl);
    }

    private async void DashboardPage_Initialized(object? sender, EventArgs e)
    {
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _loader.IsLoading = true;

        try
        {
            ScrollHost?.ScrollToTop();

            if (sensorControl != null)
            {
                sensorControl.Visibility = _dashboardSettings.Store.ShowSensors
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            _dashboardGroupControls.Clear();
            _content.ColumnDefinitions.Clear();
            _content.RowDefinitions.Clear();
            _content.Children.Clear();

            var groups = _dashboardSettings.Store.Groups ?? DashboardGroup.DefaultGroups;

            if (Log.Instance.IsTraceEnabled)
            {
                Log.Instance.Trace($"Groups:");
                foreach (var group in groups)
                    Log.Instance.Trace($" - {group}");
            }

            _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
            _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });

            var initializationTasks = new List<Task> { Task.Delay(TimeSpan.FromSeconds(1)) };
            var controls = new List<DashboardGroupControl>();

            foreach (var group in groups)
            {
                _content.RowDefinitions.Add(new RowDefinition { Height = new(1, GridUnitType.Auto) });

                var control = new DashboardGroupControl(group);
                _content.Children.Add(control);
                controls.Add(control);
                _dashboardGroupControls.Add(control);
            }

            foreach (var control in controls)
            {
                initializationTasks.Add(control.InitializedTask);
            }

            _content.RowDefinitions.Add(new RowDefinition { Height = new(1, GridUnitType.Auto) });

            var hyperlinksPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new(0, 16, 0, 0)
            };

            var editDashboardHyperlink = new Hyperlink
            {
                Icon = SymbolRegular.Edit24,
                Content = Resource.DashboardPage_Customize,
                Margin = new(0, 0, 8, 0)
            };
            editDashboardHyperlink.Click += (_, _) =>
            {
                var window = new EditDashboardWindow { Owner = Window.GetWindow(this) };
                window.Apply += async (_, _) => await RefreshAsync();
                window.ShowDialog();
            };
            hyperlinksPanel.Children.Add(editDashboardHyperlink);

            var editSensorGroupHyperlink = new Hyperlink
            {
                Icon = SymbolRegular.Edit24,
                Content = Resource.DashboardPage_Customize,
                Margin = new(8, 0, 0, 0)
            };
            editSensorGroupHyperlink.Click += (_, _) =>
            {
                var window = new EditSensorGroupWindow { Owner = Window.GetWindow(this) };
                window.Apply += async (_, _) => await RefreshAsync();
                window.ShowDialog();
            };
            hyperlinksPanel.Children.Add(editSensorGroupHyperlink);

            Grid.SetRow(hyperlinksPanel, groups.Length);
            Grid.SetColumn(hyperlinksPanel, 0);
            Grid.SetColumnSpan(hyperlinksPanel, 2);
            _content.Children.Add(hyperlinksPanel);

            LayoutGroups(ActualWidth);

            await Task.WhenAll(initializationTasks);

            App.MainWindowInstance!.SetVisual();
        }
        finally
        {
            _loader.IsLoading = false;
        }
    }

    private async Task RefreshAsyncEx()
    {
        _loader.IsLoading = true;

        var initializedTasks = new List<Task> { Task.Delay(TimeSpan.FromSeconds(1)) };

        ScrollHost?.ScrollToTop();

        if (sensorControl != null)
        {
            sensorControl.Visibility = _dashboardSettings.Store.ShowSensors ? Visibility.Visible : Visibility.Collapsed;
        }

        _dashboardGroupControls.Clear();
        _content.ColumnDefinitions.Clear();
        _content.RowDefinitions.Clear();
        _content.Children.Clear();

        var groups = _dashboardSettings.Store.Groups ?? DashboardGroup.DefaultGroups;

        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Groups:");
            foreach (var group in groups)
                Log.Instance.Trace($" - {group}");
        }

        _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
        _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });

        foreach (var group in groups)
        {
            _content.RowDefinitions.Add(new RowDefinition { Height = new(1, GridUnitType.Auto) });

            var control = new DashboardGroupControl(group);
            _content.Children.Add(control);
            _dashboardGroupControls.Add(control);
            initializedTasks.Add(control.InitializedTask);
        }

        _content.RowDefinitions.Add(new RowDefinition { Height = new(1, GridUnitType.Auto) });

        var hyperlinksPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new(0, 16, 0, 0)
        };

        var editDashboardHyperlink = new Hyperlink
        {
            Icon = SymbolRegular.Edit24,
            Content = Resource.DashboardPage_Customize,
            Margin = new(0, 0, 8, 0)
        };
        editDashboardHyperlink.Click += (_, _) =>
        {
            var window = new EditDashboardWindow { Owner = Window.GetWindow(this) };
            window.Apply += async (_, _) => await RefreshAsync();
            window.ShowDialog();
        };
        hyperlinksPanel.Children.Add(editDashboardHyperlink);

        var editSensorGroupHyperlink = new Hyperlink
        {
            Icon = SymbolRegular.Edit24,
            Content = Resource.DashboardPage_Customize,
            Margin = new(8, 0, 0, 0)
        };
        editSensorGroupHyperlink.Click += (_, _) =>
        {
            var window = new EditSensorGroupWindow { Owner = Window.GetWindow(this) };
            window.Apply += async (_, _) => await RefreshAsync();
            window.ShowDialog();
        };
        hyperlinksPanel.Children.Add(editSensorGroupHyperlink);

        Grid.SetRow(hyperlinksPanel, groups.Length);
        Grid.SetColumn(hyperlinksPanel, 0);
        Grid.SetColumnSpan(hyperlinksPanel, 2);
        _content.Children.Add(hyperlinksPanel);

        LayoutGroups(ActualWidth);

        await Task.WhenAll(initializedTasks);

        _loader.IsLoading = false;
    }

    private void DashboardPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged)
            return;

        LayoutGroups(e.NewSize.Width);
    }

    private void LayoutGroups(double width)
    {
        if (width > 1000)
            Expand();
        else
            Collapse();
    }

    private void Expand()
    {
        var lastColumn = _content.ColumnDefinitions.LastOrDefault();
        if (lastColumn is not null)
            lastColumn.Width = new(1, GridUnitType.Star);

        for (var index = 0; index < _dashboardGroupControls.Count; index++)
        {
            var control = _dashboardGroupControls[index];
            Grid.SetRow(control, index - (index % 2));
            Grid.SetColumn(control, index % 2);
        }
    }

    private void Collapse()
    {
        var lastColumn = _content.ColumnDefinitions.LastOrDefault();
        if (lastColumn is not null)
            lastColumn.Width = new(0, GridUnitType.Pixel);

        for (var index = 0; index < _dashboardGroupControls.Count; index++)
        {
            var control = _dashboardGroupControls[index];
            Grid.SetRow(control, index);
            Grid.SetColumn(control, 0);
        }
    }
}
