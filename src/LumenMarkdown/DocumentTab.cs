using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using LumenMarkdown.Core;

namespace LumenMarkdown;

internal sealed class DocumentTab : INotifyPropertyChanged, IDisposable
{
    private MarkdownDocumentSource? _source;
    private IList? _lines;
    private string _status = "Opening…";
    private bool _isLoading = true;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _changeTimer;
    private bool _watching;
    private string _searchQuery = string.Empty;
    private int[] _searchMatches = [];
    private int _currentSearchMatch = -1;
    private HeadingItem[]? _headings;
    private TableLayoutCache? _tableLayouts;
    private double _viewportWidth;

    public DocumentTab(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        Title = System.IO.Path.GetFileName(path);
        _changeTimer = new Timer(_ => ExternalChanged?.Invoke(this, EventArgs.Empty));
        _watcher = new FileSystemWatcher(System.IO.Path.GetDirectoryName(Path)!, "*")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime
        };
        _watcher.Changed += Watcher_Changed;
        _watcher.Created += Watcher_Changed;
        _watcher.Deleted += Watcher_Changed;
        _watcher.Renamed += Watcher_Renamed;
    }

    public string Path { get; }
    public string Title { get; }
    public CancellationTokenSource Cancellation { get; } = new();
    public IList? Lines { get => _lines; private set { _lines = value; OnPropertyChanged(); } }
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
    public bool IsLoading { get => _isLoading; private set { _isLoading = value; OnPropertyChanged(); } }
    public string SearchQuery { get => _searchQuery; private set { _searchQuery = value; OnPropertyChanged(); } }
    public IReadOnlyList<int> SearchMatches => _searchMatches;
    public int CurrentSearchMatch => _currentSearchMatch;
    public double ScrollOffset { get; set; }

    /// <summary>Width of the visible document area, used to wrap text and size tables consistently across rows.</summary>
    public double ViewportWidth
    {
        get => _viewportWidth;
        set
        {
            if (Math.Abs(_viewportWidth - value) < 0.5) return;
            _viewportWidth = value;
            OnPropertyChanged();
        }
    }

    public TableLayout? GetTableLayout(int lineIndex) => _tableLayouts?.Get(lineIndex);

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ExternalChanged;
    public event Action<DocumentTab, string>? LinkRequested;

    public void Attach(MarkdownDocumentSource source)
    {
        _source?.Dispose();
        _source = source;
        _tableLayouts = new TableLayoutCache(source);
        _headings = null;
        Lines = new MarkdownLineList(this, source);
        _searchMatches = [];
        _currentSearchMatch = -1;
        IsLoading = false;
        if (!_watching)
        {
            _watching = true;
            _watcher.EnableRaisingEvents = true;
        }
    }

    public void BeginReload()
    {
        Status = "Reloading…";
        IsLoading = true;
    }

    public void Fail(string message) { Status = message; IsLoading = false; }

    public async Task SearchAsync(string query, CancellationToken cancellationToken)
    {
        SearchQuery = query;
        if (_source is null || string.IsNullOrWhiteSpace(query))
        {
            _searchMatches = [];
            _currentSearchMatch = -1;
            OnPropertyChanged(nameof(SearchMatches));
            return;
        }

        var source = _source;
        var matches = await Task.Run(() =>
        {
            var found = new List<int>();
            for (var i = 0; i < source.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (source.GetLine(i).Text.Contains(query, StringComparison.OrdinalIgnoreCase)) found.Add(i);
            }
            return found.ToArray();
        }, cancellationToken);
        if (!ReferenceEquals(source, _source)) return;
        _searchMatches = matches;
        _currentSearchMatch = matches.Length > 0 ? 0 : -1;
        OnPropertyChanged(nameof(SearchMatches));
        OnPropertyChanged(nameof(CurrentSearchMatch));
    }

    public int MoveSearch(bool previous)
    {
        if (_searchMatches.Length == 0) return -1;
        _currentSearchMatch = previous
            ? (_currentSearchMatch - 1 + _searchMatches.Length) % _searchMatches.Length
            : (_currentSearchMatch + 1) % _searchMatches.Length;
        OnPropertyChanged(nameof(CurrentSearchMatch));
        return _searchMatches[_currentSearchMatch];
    }

    public int CurrentSearchLine => _currentSearchMatch >= 0 && _currentSearchMatch < _searchMatches.Length
        ? _searchMatches[_currentSearchMatch] : -1;

    public async Task<IReadOnlyList<HeadingItem>> GetHeadingsAsync(CancellationToken cancellationToken = default)
    {
        if (_headings is not null) return _headings;
        if (_source is null) return [];
        var source = _source;
        var headings = await Task.Run(() =>
        {
            var found = new List<HeadingItem>();
            for (var i = 0; i < source.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = source.GetLine(i);
                if (line.Kind is not (>= MarkdownKind.Heading1 and <= MarkdownKind.Heading6)) continue;
                var level = (int)line.Kind - (int)MarkdownKind.Heading1 + 1;
                found.Add(new HeadingItem(i, level, MarkdownNavigation.HeadingText(line)));
            }
            return found.ToArray();
        }, cancellationToken);
        if (ReferenceEquals(source, _source)) _headings = headings;
        return headings;
    }

    public void RequestLink(string target) => LinkRequested?.Invoke(this, target);

    public void Dispose()
    {
        Cancellation.Cancel();
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _changeTimer.Dispose();
        Cancellation.Dispose();
        _source?.Dispose();
    }

    private void Watcher_Changed(object sender, FileSystemEventArgs e)
    {
        if (IsWatchedPath(e.FullPath)) _changeTimer.Change(300, Timeout.Infinite);
    }

    private void Watcher_Renamed(object sender, RenamedEventArgs e)
    {
        if (IsWatchedPath(e.FullPath) || IsWatchedPath(e.OldFullPath))
            _changeTimer.Change(300, Timeout.Infinite);
    }

    private bool IsWatchedPath(string path) => string.Equals(System.IO.Path.GetFullPath(path), Path, StringComparison.OrdinalIgnoreCase);

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class MarkdownLineList(DocumentTab owner, MarkdownDocumentSource source) : IList
    {
        public int Count => source.Count;
        public object? this[int index] { get => new DocumentLineItem(owner, index, source.GetLine(index)); set => throw new NotSupportedException(); }
        public bool IsReadOnly => true; public bool IsFixedSize => true; public bool IsSynchronized => false; public object SyncRoot => this;
        public int Add(object? value) => throw new NotSupportedException(); public void Clear() => throw new NotSupportedException();
        public bool Contains(object? value) => false; public int IndexOf(object? value) => -1;
        public void Insert(int index, object? value) => throw new NotSupportedException(); public void Remove(object? value) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();
        public void CopyTo(Array array, int index) { for (var i = 0; i < Count; i++) array.SetValue(this[i], index + i); }
        public IEnumerator GetEnumerator() { for (var i = 0; i < Count; i++) yield return this[i]; }
    }
}

internal sealed record DocumentLineItem(DocumentTab Owner, int Index, MarkdownLine Line);

internal sealed record HeadingItem(int LineIndex, int Level, string Title)
{
    public string Display => $"{new string(' ', Math.Max(0, Level - 1) * 3)}{Title}";
    public string Slug => MarkdownNavigation.HeadingSlug(Title);
}
