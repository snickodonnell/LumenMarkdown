using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace LumenMarkdown;

public partial class DocumentView : UserControl
{
    private ScrollViewer? _scrollViewer;
    private DocumentTab? _tab;
    private int _restoreGeneration;
    private bool _restoring;

    public DocumentView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => Track(null);
        DataContextChanged += (_, _) =>
        {
            Track(DataContext as DocumentTab);
            PublishViewportWidth();
            RestoreOffset();
        };
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
        Track(DataContext as DocumentTab);
        _scrollViewer ??= FindVisualChild<ScrollViewer>(LinesList);
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
            _scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
        }
        PublishViewportWidth();
        RestoreOffset();
    }

    private void Track(DocumentTab? tab)
    {
        if (ReferenceEquals(tab, _tab)) return;
        if (_tab is not null) _tab.PropertyChanged -= Tab_PropertyChanged;
        _tab = tab;
        if (_tab is not null) _tab.PropertyChanged += Tab_PropertyChanged;
    }

    private void Tab_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A reload swaps the line list, which would otherwise reset the reader to the top.
        if (e.PropertyName == nameof(DocumentTab.Lines)) RestoreOffset();
    }

    private void RestoreOffset()
    {
        if (!IsLoaded || DataContext is not DocumentTab tab) return;
        var target = tab.ScrollOffset;
        var generation = ++_restoreGeneration;
        _restoring = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (generation != _restoreGeneration) return;
            _scrollViewer ??= FindVisualChild<ScrollViewer>(LinesList);
            if (ReferenceEquals(DataContext, tab)) _scrollViewer?.ScrollToVerticalOffset(target);
            Dispatcher.BeginInvoke(() => { if (generation == _restoreGeneration) _restoring = false; }, DispatcherPriority.ContextIdle);
        }, DispatcherPriority.Loaded);
    }

    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportWidthChange != 0) PublishViewportWidth();
        // Ignore the transient offsets produced while a tab switch or reload rebuilds the list.
        if (!_restoring && DataContext is DocumentTab tab) tab.ScrollOffset = e.VerticalOffset;
    }

    private void PublishViewportWidth()
    {
        _scrollViewer ??= FindVisualChild<ScrollViewer>(LinesList);
        if (_scrollViewer is not null && DataContext is DocumentTab tab && _scrollViewer.ViewportWidth > 0)
            tab.ViewportWidth = _scrollViewer.ViewportWidth;
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
