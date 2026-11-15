using System.IO;
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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static List<TodoItem> LoadTodos()
    {
        try
        {
            if (File.Exists(AppInfo.TodoFilePath))
            {
                var json = File.ReadAllText(AppInfo.TodoFilePath);
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

        // Return welcoming default tasks on first launch
        return new List<TodoItem>
        {
            new TodoItem { Title = "Welcome to NOVA Desktop Companion!", IsCompleted = true },
            new TodoItem { Title = "Play background music on YouTube Music", IsCompleted = false },
            new TodoItem { Title = "Explore Quick Notes and Focus Timer", IsCompleted = false }
        };
    }

    public static bool SaveTodos(IEnumerable<TodoItem> items)
    {
        try
        {
            var dir = AppInfo.StorageDirectory;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(items, JsonOptions);
            File.WriteAllText(AppInfo.TodoFilePath, json);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to save todos to {AppInfo.TodoFilePath}: {ex.Message}");
            return false;
        }
    }
}

