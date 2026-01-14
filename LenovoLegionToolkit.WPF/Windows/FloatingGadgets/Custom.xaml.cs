using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF.Windows.FloatingGadgets;

public class GadgetItemGroup
{
    public string Header { get; set; } = string.Empty;
    public List<FloatingGadgetItem> Items { get; set; } = new List<FloatingGadgetItem>();
}

public partial class Custom
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly SensorsGroupController _controller = IoCContainer.Resolve<SensorsGroupController>();
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
            new GadgetItemGroup { Header = Resource.FloatingGadget_Custom_Game, Items =
                [FloatingGadgetItem.Fps, FloatingGadgetItem.LowFps, FloatingGadgetItem.FrameTime]
            },
            new GadgetItemGroup { Header = Resource.FloatingGadget_Custom_CPU, Items =
                [
                    FloatingGadgetItem.CpuUtilization, FloatingGadgetItem.CpuFrequency,
                    FloatingGadgetItem.CpuTemperature,
                    FloatingGadgetItem.CpuPower, FloatingGadgetItem.CpuFan
                ]
            },
            new GadgetItemGroup { Header = Resource.FloatingGadget_Custom_GPU, Items =
                [
                    FloatingGadgetItem.GpuUtilization, FloatingGadgetItem.GpuFrequency,
                    FloatingGadgetItem.GpuTemperature,
                    FloatingGadgetItem.GpuVramTemperature, FloatingGadgetItem.GpuPower, FloatingGadgetItem.GpuFan
                ]
            },
            new GadgetItemGroup { Header = Resource.FloatingGadget_Custom_Chipset, Items =
                [
                    FloatingGadgetItem.MemoryUtilization, FloatingGadgetItem.MemoryTemperature,
                    FloatingGadgetItem.MemoryUtilization, FloatingGadgetItem.Disk1Temperature,
                    FloatingGadgetItem.MemoryUtilization, FloatingGadgetItem.Disk2Temperature,
                    FloatingGadgetItem.PchTemperature, FloatingGadgetItem.PchFan
                ]
            }
        };

        // Insert CPU P-Core and E-Core frequency options if the CPU is hybrid arch.
        var cpuGroup = groups[1];
        if (_controller.IsHybrid)
        {
            int baseFrequencyIndex = cpuGroup.Items.IndexOf(FloatingGadgetItem.CpuFrequency);
            if (baseFrequencyIndex >= 0)
            {
                cpuGroup.Items.Insert(baseFrequencyIndex + 1, FloatingGadgetItem.CpuPCoreFrequency);
                cpuGroup.Items.Insert(baseFrequencyIndex + 2, FloatingGadgetItem.CpuECoreFrequency);
                cpuGroup.Items.Remove(FloatingGadgetItem.CpuFrequency);
            }
        }

        var activeItems = new HashSet<FloatingGadgetItem>(_settings.Store.FloatingGadgetItems);

        if (activeItems.Count == 0)
        {
            activeItems = new HashSet<FloatingGadgetItem>(
                _settings.Store.FloatingGadgetItems.Cast<FloatingGadgetItem>()
            );
        }

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
                    Content = item.GetDisplayName(),
                    Tag = item,
                    IsChecked = activeItems.Contains(item)
                };
                checkBox.Checked += CheckBox_CheckedOrUnchecked;
                checkBox.Unchecked += CheckBox_CheckedOrUnchecked;

                stackPanel.Children.Add(checkBox);
            }

            groupBox.Content = stackPanel;
            _itemsStackPanel.Children.Add(groupBox);
        }
    }

    private void CheckBox_CheckedOrUnchecked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        var selectedItems = new List<FloatingGadgetItem>();

        foreach (var groupBox in _itemsStackPanel.Children.OfType<GroupBox>())
        {
            if (groupBox.Content is StackPanel stackPanel)
            {
                foreach (var child in stackPanel.Children.OfType<CheckBox>())
                {
                    if (child is { IsChecked: true, Tag: FloatingGadgetItem item })
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