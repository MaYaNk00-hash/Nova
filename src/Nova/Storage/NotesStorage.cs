using System.IO;
using Nova.Core;

namespace Nova.Storage;

public static class NotesStorage
{
    public static string LoadNotes()
    {
        try
        {
            if (File.Exists(AppInfo.NotesFilePath))
            {
                return File.ReadAllText(AppInfo.NotesFilePath);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to load quick notes from {AppInfo.NotesFilePath}: {ex.Message}");
        }

        return string.Empty;
    }

    public static bool SaveNotes(string content)
    {
        try
        {
            var dir = AppInfo.StorageDirectory;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(AppInfo.NotesFilePath, content);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to save quick notes to {AppInfo.NotesFilePath}: {ex.Message}");
            return false;
        }
    }
}
