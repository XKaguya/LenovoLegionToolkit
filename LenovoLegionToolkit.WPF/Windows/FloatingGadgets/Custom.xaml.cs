using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace LenovoLegionToolkit.WPF.Windows.FloatingGadgets;

public class GadgetItemGroup
{
    public string Header { get; set; }
    public List<FloatingGadgetItem> Items { get; set; }
}

public partial class Custom : Window
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private bool _isInitializing = true;

    public Custom()
    {
        InitializeComponent();
        this.Loaded += Custom_Loaded;
    }

    private void Custom_Loaded(object sender, RoutedEventArgs e)
    {
        InitializeCheckboxes();
        _isInitializing = false;
    }

    private void InitializeCheckboxes()
    {
        var groups = new List<GadgetItemGroup>
            {
                new GadgetItemGroup { Header = "FPS & Frame Data", Items = new List<FloatingGadgetItem> { FloatingGadgetItem.Fps, FloatingGadgetItem.LowFps, FloatingGadgetItem.FrameTime } },
                new GadgetItemGroup { Header = "CPU Metrics", Items = new List<FloatingGadgetItem> { FloatingGadgetItem.CpuUtilitazion, FloatingGadgetItem.CpuFrequency, FloatingGadgetItem.CpuTemperature, FloatingGadgetItem.CpuPower, FloatingGadgetItem.CpuFan } },
                new GadgetItemGroup { Header = "GPU Metrics", Items = new List<FloatingGadgetItem> { FloatingGadgetItem.GpuUtilitazion, FloatingGadgetItem.GpuFrequency, FloatingGadgetItem.GpuTemperature, FloatingGadgetItem.GpuVramTemperature, FloatingGadgetItem.GpuPower, FloatingGadgetItem.GpuFan } },
                new GadgetItemGroup { Header = "Memory & PCH", Items = new List<FloatingGadgetItem> { FloatingGadgetItem.MemoryUtilitazion, FloatingGadgetItem.MemoryTemperature, FloatingGadgetItem.PchTemperature, FloatingGadgetItem.PchFan } }
            };

        var activeItems = new HashSet<FloatingGadgetItem>(_settings.Store.FloatingGadgetItems);

        foreach (var group in groups)
        {
            var groupBox = new GroupBox
            {
                Header = group.Header,
                Padding = new Thickness(10, 5, 5, 10)
            };

            var stackPanel = new StackPanel();

            foreach (var item in group.Items)
            {
                var checkBox = new CheckBox
                {
                    Content = item.ToString(),
                    Tag = item,
                    IsChecked = activeItems.Contains(item)
                };
                checkBox.Checked += CheckBox_CheckedOrUnchecked;
                checkBox.Unchecked += CheckBox_CheckedOrUnchecked;

                stackPanel.Children.Add(checkBox);
            }

            groupBox.Content = stackPanel;
            ItemsStackPanel.Children.Add(groupBox);
        }
    }

    private void CheckBox_CheckedOrUnchecked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        var selectedItems = new List<FloatingGadgetItem>();

        foreach (var groupBox in ItemsStackPanel.Children.OfType<GroupBox>())
        {
            if (groupBox.Content is StackPanel stackPanel)
            {
                foreach (var child in stackPanel.Children.OfType<CheckBox>())
                {
                    if (child.IsChecked == true && child.Tag is FloatingGadgetItem item)
                    {
                        selectedItems.Add(item);
                    }
                }
            }
        }

        _settings.Store.FloatingGadgetItems = selectedItems;
        _settings.SynchronizeStore();
        MessagingCenter.Publish(new FloatingGadgetElementChangedMessage(selectedItems));
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}