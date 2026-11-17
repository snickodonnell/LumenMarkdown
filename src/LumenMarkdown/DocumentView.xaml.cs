using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace LumenMarkdown;

public partial class DocumentView : UserControl
{
    private ScrollViewer? _scrollViewer;

    public DocumentView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += (_, _) => RestoreOffset();
    }

    public void ScrollToLine(int index)
    {
        if (index < 0 || index >= LinesList.Items.Count) return;
        _scrollViewer ??= FindVisualChild<ScrollViewer>(LinesList);
        var horizontalOffset = _scrollViewer?.HorizontalOffset ?? 0;
        LinesList.ScrollIntoView(LinesList.Items[index]);
        Dispatcher.BeginInvoke(() =>
        {
            if (LinesList.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement item)
                item.BringIntoView(new Rect(0, 0, Math.Max(1, item.ActualWidth), Math.Max(1, item.ActualHeight)));
            _scrollViewer?.ScrollToHorizontalOffset(horizontalOffset);
        }, DispatcherPriority.Loaded);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scrollViewer ??= FindVisualChild<ScrollViewer>(LinesList);
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
            _scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
        }
        RestoreOffset();
    }

    private void RestoreOffset()
    {
        if (!IsLoaded || DataContext is not DocumentTab tab) return;
        Dispatcher.BeginInvoke(() =>
        {
            _scrollViewer ??= FindVisualChild<ScrollViewer>(LinesList);
            _scrollViewer?.ScrollToVerticalOffset(tab.ScrollOffset);
        }, DispatcherPriority.Loaded);
    }

    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (DataContext is DocumentTab tab) tab.ScrollOffset = e.VerticalOffset;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            if (FindVisualChild<T>(child) is { } nested) return nested;
        }
        return null;
    }
}
