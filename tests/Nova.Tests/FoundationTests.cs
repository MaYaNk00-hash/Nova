using System.IO;
using Nova.Core;

namespace Nova.Tests;

public class FoundationTests
{
    [Fact("Verify that the runtime environment is 64-bit Windows")]
    public void Environment_Is64BitAndWindows()
    {
        Assert.True(Environment.Is64BitOperatingSystem, "Operating system must be 64-bit.");
        Assert.True(Environment.Is64BitProcess, "Process must be running as 64-bit x64.");
        Assert.True(OperatingSystem.IsWindows(), "Platform must be Windows.");
        Assert.True(Environment.ProcessorCount > 0, "Processor count should be greater than 0.");
    }

    [Fact("Verify AppInfo constants and path structures")]
    public void AppInfo_MetadataIsPopulated()
    {
        Assert.Equal("NOVA", AppInfo.Name);
        Assert.Equal("0.1.0", AppInfo.Version);
        Assert.Equal("NOVA Desktop Companion", AppInfo.Description);

        Assert.False(string.IsNullOrWhiteSpace(AppInfo.AppDataDirectory), "AppData directory should not be empty.");
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.LogDirectory), "Log directory should not be empty.");
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.LogFilePath), "Log file path should not be empty.");

        Assert.True(AppInfo.LogFilePath.EndsWith("nova.log", StringComparison.OrdinalIgnoreCase), "Log file name should end with nova.log");
    }

    [Fact("Verify AppLogger writes messages correctly to disk")]
    public void AppLogger_WritesAndFormatsLogs()
    {
        var tempLogPath = Path.Combine(Path.GetTempPath(), $"nova_test_{Guid.NewGuid():N}.log");
        try
        {
            AppLogger.Initialize(tempLogPath);
            AppLogger.Info("Test info message");
            AppLogger.Warning("Test warning message");
            AppLogger.Error("Test error message", new InvalidOperationException("Simulated error"));

            Assert.True(File.Exists(tempLogPath), "Log file should exist on disk after logging.");

            var content = File.ReadAllText(tempLogPath);
            Assert.True(content.Contains("[INFO] Test info message"), "Log should contain the info line.");
            Assert.True(content.Contains("[WARNING] Test warning message"), "Log should contain the warning line.");
            Assert.True(content.Contains("[ERROR] Test error message"), "Log should contain the error line.");
            Assert.True(content.Contains("Simulated error"), "Log should contain exception message.");
        }
        finally
        {
            // Reset logger to default path
            AppLogger.Initialize(AppInfo.LogFilePath);
            if (File.Exists(tempLogPath))
            {
                try { File.Delete(tempLogPath); } catch { }
            }
        }
    }

    [Fact("Verify MainWindow can be instantiated on STA thread")]
    public void MainWindow_CanInstantiateOnStaThread()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow();
                Assert.NotNull(window);
                Assert.Equal("NOVA", window.Title);
                Assert.Equal(360, window.Height);
                Assert.Equal(520, window.Width);
                window.Close();
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (threadException != null)
        {
            throw new InvalidOperationException($"STA thread failed: {threadException.Message}", threadException);
        }
    }
}
