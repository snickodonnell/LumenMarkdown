using System.IO;
using System.Text.Json;

namespace LumenMarkdown;

internal static class Settings
{
    private static string PathName => Path.Combine(AppDataPaths.DirectoryPath, "theme.txt");
    private static string SessionPath => Path.Combine(AppDataPaths.DirectoryPath, "session.json");
    public static bool LoadDarkMode()
    {
        try { return File.Exists(PathName) ? File.ReadAllText(PathName).Trim() == "dark" : IsSystemDark(); }
        catch { return IsSystemDark(); }
    }
    public static void SaveDarkMode(bool dark)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(PathName)!); File.WriteAllText(PathName, dark ? "dark" : "light"); } catch { }
    }
    public static SessionState? LoadSession()
    {
        try { return File.Exists(SessionPath) ? JsonSerializer.Deserialize<SessionState>(File.ReadAllText(SessionPath)) : null; }
        catch { return null; }
    }

    public static void SaveSession(SessionState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SessionPath)!);
            File.WriteAllText(SessionPath, JsonSerializer.Serialize(state));
        }
        catch { }
    }
    private static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch { return false; }
    }
}

internal sealed class SessionState
{
    public string[] Paths { get; set; } = [];
    public int ActiveIndex { get; set; }
    public Dictionary<string, double> ScrollOffsets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}
