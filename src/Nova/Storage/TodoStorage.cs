using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Nova.Core;

namespace Nova.Storage;

public class TodoItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; } = false;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class TodoStorage
{
    private static readonly object _fileLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static List<TodoItem> LoadTodos()
    {
        lock (_fileLock)
        {
            try
            {
                var filePath = AppInfo.TodoFilePath;
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var items = JsonSerializer.Deserialize<List<TodoItem>>(json, JsonOptions);
                        if (items != null)
                        {
                            return items;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Failed to load todos from {AppInfo.TodoFilePath}: {ex.Message}");
            }

            // Return welcoming default tasks on very first launch only if file doesn't exist
            if (!File.Exists(AppInfo.TodoFilePath))
            {
                var defaults = new List<TodoItem>
                {
                    new TodoItem { Title = "Welcome to NOVA Desktop Companion!", IsCompleted = true },
                    new TodoItem { Title = "Play background music on YouTube Music", IsCompleted = false },
                    new TodoItem { Title = "Explore Quick Notes and Focus Timer", IsCompleted = false }
                };
                SaveTodos(defaults);
                return defaults;
            }

            return new List<TodoItem>();
        }
    }

    public static bool SaveTodos(IEnumerable<TodoItem> items)
    {
        lock (_fileLock)
        {
            try
            {
                var dir = AppInfo.StorageDirectory;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var list = items.ToList();
                var json = JsonSerializer.Serialize(list, JsonOptions);
                var filePath = AppInfo.TodoFilePath;
                var tmpPath = filePath + ".tmp";

                // Atomic write to avoid partial or corrupted writes
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, filePath, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Failed to save todos to {AppInfo.TodoFilePath}: {ex.Message}");
                return false;
            }
        }
    }
}
