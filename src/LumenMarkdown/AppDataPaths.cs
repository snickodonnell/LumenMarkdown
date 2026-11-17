using System.IO;

namespace LumenMarkdown;

internal static class AppDataPaths
{
    public static string DirectoryPath
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable("LUMENMARKDOWN_DATA_DIR");
            return string.IsNullOrWhiteSpace(overridePath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumenMarkdown")
                : Path.GetFullPath(overridePath);
        }
    }
}
