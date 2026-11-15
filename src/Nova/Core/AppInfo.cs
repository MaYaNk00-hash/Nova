using System.IO;

namespace Nova.Core;

public static class AppInfo
{
    public const string Name = "NOVA";
    public const string Version = "0.1.0";
    public const string Description = "NOVA Desktop Companion";

    public static string AppDataDirectory
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable("NOVA_APPDATA_DIR");
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return overridePath;
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nova");
        }
    }

    public static string LogDirectory =>
        Path.Combine(AppDataDirectory, "Logs");

    public static string LogFilePath =>
        Path.Combine(LogDirectory, "nova.log");

    public static string StorageDirectory =>
        Path.Combine(AppDataDirectory, "Storage");

    public static string NotesFilePath =>
        Path.Combine(StorageDirectory, "quick_notes.txt");

    public static string TodoFilePath =>
        Path.Combine(StorageDirectory, "todos.json");
}
