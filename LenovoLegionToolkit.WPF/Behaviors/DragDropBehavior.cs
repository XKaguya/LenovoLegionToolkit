using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LenovoLegionToolkit.WPF.Behaviors
{
    public static class DragDropBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(DragDropBehavior), new UIPropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        private static Point _startPoint;
        private static FrameworkElement? _draggedElement;
        private static Panel? _sourcePanel;

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Panel panel)
                return;

            if ((bool)e.NewValue)
            {
                panel.AllowDrop = true;
                panel.MouseLeftButtonDown += OnMouseLeftButtonDown;
                panel.MouseMove += OnMouseMove;
                panel.Drop += OnDrop;
            }
            else
            {
                panel.AllowDrop = false;
                panel.MouseLeftButtonDown -= OnMouseLeftButtonDown;
                panel.MouseMove -= OnMouseMove;
                panel.Drop -= OnDrop;
            }
        }

        private static void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Panel panel)
                return;

            _startPoint = e.GetPosition(panel);
            _sourcePanel = panel;

            if (e.OriginalSource is FrameworkElement element && panel.Children.Contains(element))
            {
                _draggedElement = element;
                Console.WriteLine($"Element to drag: {_draggedElement}");
                e.Handled = true;
            }
            else if (e.OriginalSource is Visual visual)
            {
                _draggedElement = FindParentInPanel(panel, visual);
                Console.WriteLine($"Element to drag: {_draggedElement}");
                e.Handled = true;
            }
        }

        private static void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedElement != null)
            {
                var currentPoint = e.GetPosition(_sourcePanel);

                if (Math.Abs(currentPoint.X - _startPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(currentPoint.Y - _startPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var data = new DataObject(typeof(FrameworkElement), _draggedElement);
                    DragDrop.DoDragDrop(_draggedElement, data, DragDropEffects.Move);

                    _draggedElement = null;
                    _sourcePanel = null;
                }
            }
        }

        private static void OnDrop(object sender, DragEventArgs e)
        {
            if (sender is not Panel panel || !e.Data.GetDataPresent(typeof(FrameworkElement)))
                return;

            var droppedElement = e.Data.GetData(typeof(FrameworkElement)) as FrameworkElement;
            if (droppedElement == null || !panel.Children.Contains(droppedElement))
                return;

            var dropPosition = e.GetPosition(panel);
            int newIndex = -1;

            for (int i = 0; i < panel.Children.Count; i++)
            {
                var child = panel.Children[i] as FrameworkElement;
                if (child == null) continue;

                var childCenter = child.TransformToAncestor(panel).Transform(new Point(child.ActualWidth / 2, child.ActualHeight / 2));

                if (panel is StackPanel { Orientation: Orientation.Horizontal })
                {
                    if (dropPosition.X < childCenter.X)
                    {
                        newIndex = i;
                        break;
                    }
                }
                else
                {
                    if (dropPosition.Y < childCenter.Y)
                    {
                        newIndex = i;
                        break;
                    }
                }
            }

            if (newIndex >= 0)
            {
                int currentIndex = panel.Children.IndexOf(droppedElement);
                if (currentIndex == newIndex) return;

                panel.Children.Remove(droppedElement);
                panel.Children.Insert(newIndex, droppedElement);
            }
            else
            {
                panel.Children.Remove(droppedElement);
                panel.Children.Add(droppedElement);
            }
        }

        private static FrameworkElement? FindParentInPanel(Panel panel, Visual child)
        {
            DependencyObject? parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is FrameworkElement frameworkElement && panel.Children.Contains(frameworkElement))
                {
                    return frameworkElement;
                }
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}
