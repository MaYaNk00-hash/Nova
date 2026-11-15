using System.IO;
using Nova.Core;
using Nova.Storage;
using Nova.SystemMonitor;

namespace Nova.Tests;

public class NotesAndTodoTests
{
    [Fact("Verify Quick Notes storage saves and loads accurately")]
    public void NotesStorage_SaveAndLoad_PreservesText()
    {
        var testNote = $"Meeting ideas at {DateTime.UtcNow:O}\n- Review Nova Island HUD\n- Check YouTube Music controls";
        
        bool saved = NotesStorage.SaveNotes(testNote);
        Assert.True(saved, "NotesStorage.SaveNotes should succeed.");

        string loaded = NotesStorage.LoadNotes();
        Assert.Equal(testNote, loaded);
    }

    [Fact("Verify To-Do list storage saves and loads task collection accurately")]
    public void TodoStorage_SaveAndLoad_PreservesItems()
    {
        var items = new List<TodoItem>
        {
            new TodoItem { Title = "Ship Nova v0.1.0 HUD", IsCompleted = false },
            new TodoItem { Title = "Configure System Monitor bars", IsCompleted = true }
        };

        bool saved = TodoStorage.SaveTodos(items);
        Assert.True(saved, "TodoStorage.SaveTodos should succeed.");

        var loaded = TodoStorage.LoadTodos();
        Assert.NotNull(loaded);
        Assert.True(loaded.Count >= 2, "Loaded todos should contain at least 2 items.");
        Assert.Contains(loaded, t => t.Title == "Ship Nova v0.1.0 HUD" && !t.IsCompleted);
        Assert.Contains(loaded, t => t.Title == "Configure System Monitor bars" && t.IsCompleted);
    }

    [Fact("Verify SystemMonitorService produces valid non-zero hardware metrics")]
    public void SystemMonitor_GetCurrentSnapshot_ReturnsValidRanges()
    {
        var monitor = new SystemMonitorService();
        var snapshot = monitor.GetCurrentSnapshot();

        Assert.NotNull(snapshot);
        Assert.True(snapshot.CpuPercent >= 0.0 && snapshot.CpuPercent <= 100.0, "CPU percent should be between 0 and 100.");
        Assert.True(snapshot.RamPercent >= 0.0 && snapshot.RamPercent <= 100.0, "RAM percent should be between 0 and 100.");
        Assert.True(snapshot.RamTotalGb > 0.0, "Total RAM should be greater than 0 GB.");
        Assert.True(snapshot.RamUsedGb >= 0.0, "Used RAM should be non-negative.");
        Assert.True(snapshot.DiskTotalGb > 0.0, "Total Disk should be greater than 0 GB.");
    }
}

