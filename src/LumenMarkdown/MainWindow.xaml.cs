using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LumenMarkdown.Core;
using Microsoft.Win32;

namespace LumenMarkdown;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<DocumentTab> _tabs = [];
    private readonly IReadOnlyList<string> _initialPaths;
    private bool _dark;
    private int _toastGeneration;
    private readonly SessionState? _session;
    private CancellationTokenSource? _findCancellation;
    private bool _settingFindText;

    public MainWindow(IReadOnlyList<string> initialPaths)
    {
        _session = Settings.LoadSession();
        _initialPaths = initialPaths.Count > 0 ? initialPaths : _session?.Paths ?? [];
        InitializeComponent();
        DocumentTabs.ItemsSource = _tabs;
        _dark = Settings.LoadDarkMode();
        ApplyTheme();
        PreviewKeyDown += OnPreviewKeyDown;
        DragEnter += OnDragEnter;
        Drop += OnDrop;
        Closing += OnClosing;
        Loaded += OnLoaded;
        RestoreWindowBounds();
        UpdateWindowState();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialPaths.Count > 0)
        {
            await OpenFilesAsync(_initialPaths);
            if (_session is not null && _tabs.Count > 0 && _session.ActiveIndex >= 0)
                DocumentTabs.SelectedIndex = Math.Min(_session.ActiveIndex, _tabs.Count - 1);
        }
    }

    public async Task OpenFilesAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(File.Exists)) await OpenFileAsync(path);
    }

    public async Task OpenFileAsync(string path)
    {
        path = Path.GetFullPath(path);
        var existing = _tabs.FirstOrDefault(tab => string.Equals(tab.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            DocumentTabs.SelectedItem = existing;
            RecentFiles.Add(path);
            UpdateWindowState();
            return;
        }

        var tab = new DocumentTab(path);
        if (_session?.ScrollOffsets.TryGetValue(path, out var offset) == true) tab.ScrollOffset = Math.Max(0, offset);
        tab.PropertyChanged += Tab_PropertyChanged;
        tab.ExternalChanged += Tab_ExternalChanged;
        tab.LinkRequested += Tab_LinkRequested;
        _tabs.Add(tab);
        DocumentTabs.SelectedItem = tab;
        UpdateWindowState();

        var progress = new Progress<double>(value =>
        {
            tab.Status = $"Indexing… {value:P0}";
            if (ReferenceEquals(DocumentTabs.SelectedItem, tab)) StatusText.Text = tab.Status;
        });

        try
        {
            var source = await MarkdownDocumentSource.OpenAsync(path, progress, tab.Cancellation.Token);
            if (!_tabs.Contains(tab) || tab.Cancellation.IsCancellationRequested)
            {
                source.Dispose();
                return;
            }

            tab.Attach(source);
            tab.Status = $"{source.Count:N0} lines  ·  {FormatSize(source.SizeBytes)}  ·  {path}";
            RecentFiles.Add(path);
            UpdateWindowState();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_tabs.Contains(tab)) return;
            tab.Fail("Could not open file");
            UpdateWindowState();
            MessageBox.Show(this, ex.Message, "Lumen Markdown", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Markdown files (*.md;*.markdown)|*.md;*.markdown|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) == true) await OpenFilesAsync(dialog.FileNames);
    }

    private void FileMenuButton_Click(object sender, RoutedEventArgs e)
    {
        FileContextMenu.PlacementTarget = FileMenuButton;
        FileContextMenu.Placement = PlacementMode.Bottom;
        FileContextMenu.IsOpen = true;
    }

    private void FileContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var hasActive = DocumentTabs.SelectedItem is DocumentTab;
        SaveAsMenu.IsEnabled = hasActive;
        ReloadMenu.IsEnabled = hasActive;
        RevealMenu.IsEnabled = hasActive;
        CopyPathMenu.IsEnabled = hasActive;
        CopyRawMenu.IsEnabled = hasActive;
        CloseTabMenu.IsEnabled = hasActive;
        CloseAllMenu.IsEnabled = _tabs.Count > 0;

        OpenRecentMenu.Items.Clear();
        var recent = RecentFiles.GetAll();
        if (recent.Count == 0)
        {
            OpenRecentMenu.Items.Add(new MenuItem { Header = "No recent files", IsEnabled = false });
            return;
        }

        foreach (var path in recent)
        {
            var parent = Path.GetFileName(Path.GetDirectoryName(path)) ?? Path.GetDirectoryName(path) ?? string.Empty;
            var item = new MenuItem
            {
                Header = EscapeMenuText($"{Path.GetFileName(path)}  —  {parent}"),
                ToolTip = path,
                Tag = path,
                IsEnabled = File.Exists(path)
            };
            item.Click += RecentItem_Click;
            OpenRecentMenu.Items.Add(item);
        }

        OpenRecentMenu.Items.Add(new Separator());
        var clear = new MenuItem { Header = "Clear Recent Files" };
        clear.Click += (_, _) => RecentFiles.Clear();
        OpenRecentMenu.Items.Add(clear);
    }

    private async void RecentItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.Tag is not string path) return;
        if (!File.Exists(path))
        {
            RecentFiles.Remove(path);
            MessageBox.Show(this, "That file is no longer available.", "Lumen Markdown", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await OpenFileAsync(path);
    }

    private async void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (DocumentTabs.SelectedItem is not DocumentTab tab) return;
        var dialog = new SaveFileDialog
        {
            FileName = Path.GetFileName(tab.Path),
            InitialDirectory = Path.GetDirectoryName(tab.Path),
            Filter = "Markdown files (*.md)|*.md|Markdown files (*.markdown)|*.markdown|All files (*.*)|*.*",
            AddExtension = true,
            DefaultExt = ".md",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        if (string.Equals(Path.GetFullPath(dialog.FileName), tab.Path, StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "Choose a different destination for Save As.";
            return;
        }

        var previousStatus = tab.Status;
        tab.Status = "Saving copy…";
        try
        {
            await MarkdownFileOperations.CopyAsync(tab.Path, dialog.FileName, tab.Cancellation.Token);
            tab.Status = previousStatus;
            RecentFiles.Add(dialog.FileName);
            await OpenFileAsync(dialog.FileName);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            tab.Status = "Save As failed";
            MessageBox.Show(this, ex.Message, "Lumen Markdown", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (DocumentTabs.SelectedItem is DocumentTab tab) await ReloadTabAsync(tab);
    }

    private async Task<bool> ReloadTabAsync(DocumentTab tab, bool showError = true)
    {
        if (!File.Exists(tab.Path))
        {
            tab.Fail("File deleted or moved on disk");
            UpdateWindowState();
            return false;
        }
        tab.BeginReload();
        var progress = new Progress<double>(value => tab.Status = $"Reloading… {value:P0}");
        try
        {
            var source = await MarkdownDocumentSource.OpenAsync(tab.Path, progress, tab.Cancellation.Token);
            if (!_tabs.Contains(tab)) { source.Dispose(); return false; }
            tab.Attach(source);
            tab.Status = $"{source.Count:N0} lines  ·  {FormatSize(source.SizeBytes)}  ·  {tab.Path}";
            RecentFiles.Add(tab.Path);
            UpdateWindowState();
            if (!string.IsNullOrWhiteSpace(tab.SearchQuery)) await RunSearchAsync(tab, tab.SearchQuery, navigateToFirst: false);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            tab.Fail("Reload failed");
            if (showError) MessageBox.Show(this, ex.Message, "Lumen Markdown", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void Tab_ExternalChanged(object? sender, EventArgs e)
    {
        if (sender is not DocumentTab tab) return;
        Dispatcher.InvokeAsync(async () =>
        {
            if (_tabs.Contains(tab) && await ReloadTabAsync(tab, showError: false)) ShowToast("File updated");
        });
    }

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (DocumentTabs.SelectedItem is not DocumentTab tab) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{tab.Path}\"") { UseShellExecute = true });
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (DocumentTabs.SelectedItem is not DocumentTab tab) return;
        try { Clipboard.SetText(tab.Path); StatusText.Text = "File path copied."; }
        catch { StatusText.Text = "Could not access the clipboard."; }
    }

    private async void CopyRaw_Click(object sender, RoutedEventArgs e)
    {
        if (DocumentTabs.SelectedItem is DocumentTab tab) await CopyRawMarkdownAsync(tab);
    }

    private void CloseActiveTab_Click(object sender, RoutedEventArgs e)
    {
        if (DocumentTabs.SelectedItem is DocumentTab tab) CloseTab(tab);
    }

    private void CloseAllTabs_Click(object sender, RoutedEventArgs e)
    {
        foreach (var tab in _tabs.ToArray()) CloseTab(tab);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is DocumentTab tab) CloseTab(tab);
    }

    private void CloseTab(DocumentTab tab)
    {
        var index = _tabs.IndexOf(tab);
        if (index < 0) return;
        tab.PropertyChanged -= Tab_PropertyChanged;
        tab.ExternalChanged -= Tab_ExternalChanged;
        tab.LinkRequested -= Tab_LinkRequested;
        _tabs.RemoveAt(index);
        tab.Dispose();
        if (_tabs.Count > 0) DocumentTabs.SelectedIndex = Math.Min(index, _tabs.Count - 1);
        UpdateWindowState();
    }

    private void DocumentTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateWindowState();
        if (FindPanel.Visibility == Visibility.Visible && DocumentTabs.SelectedItem is DocumentTab tab)
        {
            _settingFindText = true;
            FindBox.Text = tab.SearchQuery;
            _settingFindText = false;
            UpdateFindCount(tab);
        }
    }

    private void Tab_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, DocumentTabs.SelectedItem) && e.PropertyName == nameof(DocumentTab.Status))
            StatusText.Text = ((DocumentTab)sender!).Status;
    }

    private void UpdateWindowState()
    {
        var active = DocumentTabs.SelectedItem as DocumentTab;
        EmptyState.Visibility = _tabs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DocumentTabs.Visibility = _tabs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        FileTitle.Text = active?.Title ?? "Lumen Markdown";
        StatusText.Text = active?.Status ?? "Ready";
        Title = active is null
            ? "Lumen Markdown"
            : _tabs.Count > 1
                ? $"{active.Title} — Lumen Markdown ({_tabs.Count} tabs)"
                : $"{active.Title} — Lumen Markdown";
    }

    private void Theme_Click(object sender, RoutedEventArgs e) { _dark = !_dark; ApplyTheme(); Settings.SaveDarkMode(_dark); }
    private void ApplyTheme()
    {
        var colors = _dark
            ? new[] { "#202123", "#2A2B2E", "#343538", "#ECECF1", "#A9A9B2", "#3D3E42", "#2D2E32", "#19C597" }
            : new[] { "#F7F7F5", "#FFFFFF", "#ECECEA", "#202123", "#6F7074", "#DEDEDA", "#EEEEEA", "#10A37F" };
        var keys = new[] { "PageBrush", "SurfaceBrush", "HoverBrush", "TextBrush", "MutedTextBrush", "SubtleBrush", "CodeBrush", "AccentBrush" };
        for (var i = 0; i < keys.Length; i++) Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        ThemeButton.Content = _dark ? "☀" : "☾";
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) await OpenFilesAsync(files);
    }

    private void OnDragEnter(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A && DocumentTabs.SelectedItem is DocumentTab copyTab)
        {
            e.Handled = true;
            await CopyRawMarkdownAsync(copyTab);
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F) { e.Handled = true; ShowFind(); }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O) { e.Handled = true; Open_Click(sender, e); }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.T) { e.Handled = true; Theme_Click(sender, e); }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S) { e.Handled = true; SaveAs_Click(sender, e); }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.R && DocumentTabs.SelectedItem is DocumentTab reloadTab) { e.Handled = true; _ = ReloadTabAsync(reloadTab); }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.E) { e.Handled = true; Reveal_Click(sender, e); }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.O) { e.Handled = true; await ShowOutlineAsync(); }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Tab) { e.Handled = true; SelectAdjacentTab(-1); }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Tab) { e.Handled = true; SelectAdjacentTab(1); }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.W) { e.Handled = true; CloseAllTabs_Click(sender, e); }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W && DocumentTabs.SelectedItem is DocumentTab tab) { e.Handled = true; CloseTab(tab); }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is >= Key.D1 and <= Key.D9)
        {
            e.Handled = true;
            var index = (int)e.Key - (int)Key.D1;
            if (index < _tabs.Count) DocumentTabs.SelectedIndex = index;
        }
    }

    private async Task CopyRawMarkdownAsync(DocumentTab tab)
    {
        try
        {
            var rawText = await MarkdownFileOperations.ReadAllTextAsync(tab.Path, tab.Cancellation.Token);
            if (rawText.Length == 0) Clipboard.Clear();
            else Clipboard.SetText(rawText);
            ShowToast("Raw Markdown copied");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ShowToast("Copy failed");
            MessageBox.Show(this, ex.Message, "Lumen Markdown", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowFind()
    {
        if (DocumentTabs.SelectedItem is not DocumentTab) return;
        FindPanel.Visibility = Visibility.Visible;
        FindBox.Focus();
        FindBox.SelectAll();
    }

    private async void FindBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_settingFindText || DocumentTabs.SelectedItem is not DocumentTab tab) return;
        _findCancellation?.Cancel();
        _findCancellation?.Dispose();
        _findCancellation = CancellationTokenSource.CreateLinkedTokenSource(tab.Cancellation.Token);
        var token = _findCancellation.Token;
        try
        {
            await Task.Delay(160, token);
            await RunSearchAsync(tab, FindBox.Text, navigateToFirst: true, token);
        }
        catch (OperationCanceledException) { }
    }

    private async Task RunSearchAsync(DocumentTab tab, string query, bool navigateToFirst, CancellationToken token = default)
    {
        await tab.SearchAsync(query, token);
        if (!ReferenceEquals(DocumentTabs.SelectedItem, tab)) return;
        UpdateFindCount(tab);
        if (navigateToFirst && tab.CurrentSearchLine >= 0) ScrollToLine(tab.CurrentSearchLine);
    }

    private void UpdateFindCount(DocumentTab tab)
    {
        FindCountText.Text = tab.SearchMatches.Count == 0
            ? "0 results"
            : $"{Math.Max(1, tab.CurrentSearchMatch + 1)} of {tab.SearchMatches.Count:N0}";
    }

    private void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; CloseFind(); }
        else if (e.Key == Key.Enter) { e.Handled = true; MoveFind(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); }
    }

    private void FindPrevious_Click(object sender, RoutedEventArgs e) => MoveFind(true);
    private void FindNext_Click(object sender, RoutedEventArgs e) => MoveFind(false);
    private void FindClose_Click(object sender, RoutedEventArgs e) => CloseFind();

    private void MoveFind(bool previous)
    {
        if (DocumentTabs.SelectedItem is not DocumentTab tab) return;
        var line = tab.MoveSearch(previous);
        UpdateFindCount(tab);
        if (line >= 0) ScrollToLine(line);
    }

    private void CloseFind()
    {
        FindPanel.Visibility = Visibility.Collapsed;
        _findCancellation?.Cancel();
        if (DocumentTabs.SelectedItem is DocumentTab tab) _ = tab.SearchAsync(string.Empty, tab.Cancellation.Token);
        DocumentTabs.Focus();
    }

    private async void Outline_Click(object sender, RoutedEventArgs e) => await ShowOutlineAsync();

    private async Task ShowOutlineAsync()
    {
        if (DocumentTabs.SelectedItem is not DocumentTab tab) return;
        OutlineList.ItemsSource = null;
        OutlineEmptyText.Text = "Loading headings…";
        OutlineEmptyText.Visibility = Visibility.Visible;
        OutlinePopup.IsOpen = true;
        try
        {
            var headings = await tab.GetHeadingsAsync(tab.Cancellation.Token);
            if (!ReferenceEquals(DocumentTabs.SelectedItem, tab) || !OutlinePopup.IsOpen) return;
            OutlineList.ItemsSource = headings;
            OutlineEmptyText.Text = "No headings in this document";
            OutlineEmptyText.Visibility = headings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
    }

    private void OutlineList_Click(object sender, MouseButtonEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element is not null && element is not ListBoxItem)
            element = VisualTreeHelper.GetParent(element);
        if (element is ListBoxItem { DataContext: HeadingItem heading })
        {
            ScrollToLine(heading.LineIndex);
            OutlinePopup.IsOpen = false;
        }
    }

    private void TabHeader_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || (sender as FrameworkElement)?.DataContext is not DocumentTab tab) return;
        e.Handled = true;
        CloseTab(tab);
    }

    private void SelectAdjacentTab(int direction)
    {
        if (_tabs.Count == 0) return;
        var current = Math.Max(0, DocumentTabs.SelectedIndex);
        DocumentTabs.SelectedIndex = (current + direction + _tabs.Count) % _tabs.Count;
    }

    private void ScrollToLine(int line)
    {
        if (FindVisualChild<DocumentView>(DocumentTabs) is { } view) view.ScrollToLine(line);
    }

    private async void Tab_LinkRequested(DocumentTab tab, string target)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(target)) return;
            var fragmentIndex = target.IndexOf('#');
            var pathPart = fragmentIndex >= 0 ? target[..fragmentIndex] : target;
            var fragment = fragmentIndex >= 0 ? target[(fragmentIndex + 1)..] : string.Empty;

            if (target.StartsWith('#'))
            {
                await NavigateToHeadingAsync(tab, fragment);
                return;
            }

            if (Uri.TryCreate(target, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https" or "mailto")
            {
                Process.Start(new ProcessStartInfo(absolute.AbsoluteUri) { UseShellExecute = true });
                return;
            }

            var decodedPath = Uri.UnescapeDataString(pathPart);
            var resolved = absolute is { IsFile: true }
                ? absolute.LocalPath
                : Path.IsPathRooted(decodedPath)
                    ? Path.GetFullPath(decodedPath)
                    : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(tab.Path)!, decodedPath));
            if (!File.Exists(resolved)) { ShowToast("Linked file not found"); return; }

            if (Path.GetExtension(resolved).Equals(".md", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(resolved).Equals(".markdown", StringComparison.OrdinalIgnoreCase))
            {
                await OpenFileAsync(resolved);
                if (!string.IsNullOrEmpty(fragment) && DocumentTabs.SelectedItem is DocumentTab linkedTab)
                    await NavigateToHeadingAsync(linkedTab, fragment);
            }
            else Process.Start(new ProcessStartInfo(resolved) { UseShellExecute = true });
        }
        catch { ShowToast("Could not open link"); }
    }

    private async Task NavigateToHeadingAsync(DocumentTab tab, string fragment)
    {
        var slug = MarkdownNavigation.HeadingSlug(Uri.UnescapeDataString(fragment));
        var headings = await tab.GetHeadingsAsync(tab.Cancellation.Token);
        var heading = headings.FirstOrDefault(item => item.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
        if (heading is null) { ShowToast("Heading not found"); return; }
        if (!ReferenceEquals(DocumentTabs.SelectedItem, tab)) DocumentTabs.SelectedItem = tab;
        ScrollToLine(heading.LineIndex);
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

    private async void ShowToast(string message)
    {
        var generation = ++_toastGeneration;
        CopyToastText.Text = message;
        CopyToast.Visibility = Visibility.Visible;
        CopyToast.BeginAnimation(OpacityProperty, null);
        CopyToast.Opacity = 0;
        CopyToast.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)) { EasingFunction = new QuadraticEase() });

        await Task.Delay(1450);
        if (generation != _toastGeneration) return;

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase() };
        fade.Completed += (_, _) =>
        {
            if (generation == _toastGeneration) CopyToast.Visibility = Visibility.Collapsed;
        };
        CopyToast.BeginAnimation(OpacityProperty, fade);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void RestoreWindowBounds()
    {
        if (_session is null || _session.Width < MinWidth || _session.Height < MinHeight) return;
        Width = Math.Min(_session.Width, SystemParameters.VirtualScreenWidth);
        Height = Math.Min(_session.Height, SystemParameters.VirtualScreenHeight);
        var right = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
        var bottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
        if (_session.Left < right - 80 && _session.Left + Width > SystemParameters.VirtualScreenLeft + 80 &&
            _session.Top < bottom - 60 && _session.Top + Height > SystemParameters.VirtualScreenTop + 60)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _session.Left;
            Top = _session.Top;
        }
        if (_session.Maximized) WindowState = WindowState.Maximized;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _findCancellation?.Cancel();
        _findCancellation?.Dispose();
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        Settings.SaveSession(new SessionState
        {
            Paths = _tabs.Select(tab => tab.Path).ToArray(),
            ActiveIndex = Math.Max(0, DocumentTabs.SelectedIndex),
            ScrollOffsets = _tabs.ToDictionary(tab => tab.Path, tab => tab.ScrollOffset, StringComparer.OrdinalIgnoreCase),
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = WindowState == WindowState.Maximized
        });
        foreach (var tab in _tabs.ToArray())
        {
            tab.PropertyChanged -= Tab_PropertyChanged;
            tab.ExternalChanged -= Tab_ExternalChanged;
            tab.LinkRequested -= Tab_LinkRequested;
            tab.Dispose();
        }
        _tabs.Clear();
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824d:0.##} GB",
        >= 1_048_576 => $"{bytes / 1_048_576d:0.##} MB",
        >= 1024 => $"{bytes / 1024d:0.##} KB",
        _ => $"{bytes} B"
    };

    private static string EscapeMenuText(string value) => value.Replace("_", "__", StringComparison.Ordinal);
}
