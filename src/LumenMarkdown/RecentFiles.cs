using System.IO;
using System.Text.Json;

namespace LumenMarkdown;

internal static class RecentFiles
{
    private const int MaximumEntries = 12;
    private static readonly object Gate = new();
    private static readonly string StoragePath = Path.Combine(AppDataPaths.DirectoryPath, "recent-files.json");

    public static IReadOnlyList<string> GetAll()
    {
        lock (Gate) return Load().ToArray();
    }

    public static void Add(string path)
    {
        path = Path.GetFullPath(path);
        lock (Gate)
        {
            var items = Load();
            items.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
            items.Insert(0, path);
            if (items.Count > MaximumEntries) items.RemoveRange(MaximumEntries, items.Count - MaximumEntries);
            Save(items);
        }
    }

    public static void Remove(string path)
    {
        lock (Gate)
        {
            var items = Load();
            items.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
            Save(items);
        }
    }

    public static void Clear()
    {
        lock (Gate) Save([]);
    }

    private static List<string> Load()
    {
        try
        {
            if (!File.Exists(StoragePath)) return [];
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(StoragePath)) ?? [];
        }
        catch { return []; }
    }

    private static void Save(List<string> items)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
            var temporary = StoragePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(items));
            File.Move(temporary, StoragePath, overwrite: true);
        }
        catch { }
    }
}
