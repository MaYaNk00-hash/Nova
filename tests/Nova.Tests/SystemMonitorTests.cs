using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nova.SystemMonitor;

namespace Nova.Tests;

public class SystemMonitorTests
{
    // =========================================================================
    // SECTION A: PURE CALCULATION & BOUNDARY TESTS (MOCKED / DETERMINISTIC)
    // These tests do NOT rely on real Windows API state or environment conditions.
    // =========================================================================

    [Fact("CPU Calculation: 100% idle ticks yield 0% CPU")]
    public void CpuCalculation_FullIdle_YieldsZeroPercent()
    {
        // TotalSys = diffKernel(100) + diffUser(0) = 100. diffIdle = 100.
        // Busy = (100 - 100) / 100 = 0%
        double cpu = SystemMonitorService.CalculateCpuPercent(
            prevIdle: 1000, prevKernel: 2000, prevUser: 500,
            currentIdle: 1100, currentKernel: 2100, currentUser: 500);

        Assert.Equal(0.0, cpu);
    }

    [Fact("CPU Calculation: 0% idle ticks yield 100% CPU")]
    public void CpuCalculation_ZeroIdle_YieldsHundredPercent()
    {
        // TotalSys = diffKernel(20) + diffUser(80) = 100. diffIdle = 0.
        // Busy = (100 - 0) / 100 = 100%
        double cpu = SystemMonitorService.CalculateCpuPercent(
            prevIdle: 1000, prevKernel: 2000, prevUser: 500,
            currentIdle: 1000, currentKernel: 2020, currentUser: 580);

        Assert.Equal(100.0, cpu);
    }

    [Fact("CPU Calculation: 50% idle ticks yield 50% CPU")]
    public void CpuCalculation_HalfIdle_YieldsFiftyPercent()
    {
        // TotalSys = diffKernel(60) + diffUser(40) = 100. diffIdle = 50.
        // Busy = (100 - 50) / 100 = 50%
        double cpu = SystemMonitorService.CalculateCpuPercent(
            prevIdle: 1000, prevKernel: 2000, prevUser: 500,
            currentIdle: 1050, currentKernel: 2060, currentUser: 540);

        Assert.Equal(50.0, cpu);
    }

    [Fact("CPU Calculation: Multi-core timer jitter (idle > totalSys) guards against unsigned underflow")]
    public void CpuCalculation_UnderflowProtection_ReturnsZero()
    {
        // If diffIdle (120) > totalSys (100), unsigned subtraction would underflow to ~18 quintillion.
        // Guard must catch this and return 0.0%.
        double cpu = SystemMonitorService.CalculateCpuPercent(
            prevIdle: 1000, prevKernel: 2000, prevUser: 500,
            currentIdle: 1120, currentKernel: 2050, currentUser: 550);

        Assert.Equal(0.0, cpu);
    }

    [Fact("CPU Calculation: Zero elapsed ticks between samples returns 0%")]
    public void CpuCalculation_ZeroElapsed_ReturnsZero()
    {
        double cpu = SystemMonitorService.CalculateCpuPercent(
            prevIdle: 1000, prevKernel: 2000, prevUser: 500,
            currentIdle: 1000, currentKernel: 2000, currentUser: 500);

        Assert.Equal(0.0, cpu);
    }

    [Fact("RAM Calculation: Used memory never exceeds total memory")]
    public void RamCalculation_Consistency_UsedNeverExceedsTotal()
    {
        // Total: 16 GB, Available: 4 GB
        ulong totalBytes = 16UL * 1024 * 1024 * 1024;
        ulong availBytes = 4UL * 1024 * 1024 * 1024;
        var (load, usedGb, totalGb) = SystemMonitorService.CalculateRamMetrics(totalBytes, availBytes, 75);

        Assert.Equal(75.0, load);
        Assert.Equal(16.0, totalGb);
        Assert.Equal(12.0, usedGb);
        Assert.True(usedGb <= totalGb, "Used RAM must not exceed total RAM.");
        Assert.True(usedGb >= 0.0, "Used RAM must be non-negative.");
    }

    [Fact("RAM Calculation: Zero total bytes handles cleanly without division-by-zero")]
    public void RamCalculation_ZeroBytes_HandlesCleanly()
    {
        var (load, usedGb, totalGb) = SystemMonitorService.CalculateRamMetrics(0, 0, 0);

        Assert.Equal(0.0, load);
        Assert.Equal(0.0, usedGb);
        Assert.Equal(0.0, totalGb);
    }

    [Fact("Disk Calculation: Accurately reflects storage volume capacity usage")]
    public void DiskCalculation_StorageCapacity_CalculatesCorrectly()
    {
        // Total: 500 GB, Free: 100 GB -> Used: 400 GB (80%)
        long totalBytes = 500L * 1024 * 1024 * 1024;
        long freeBytes = 100L * 1024 * 1024 * 1024;
        var (pct, usedGb, totalGb) = SystemMonitorService.CalculateDiskMetrics(totalBytes, freeBytes);

        Assert.Equal(80.0, pct);
        Assert.Equal(500.0, totalGb);
        Assert.Equal(400.0, usedGb);
        Assert.True(usedGb <= totalGb, "Used disk capacity must not exceed total capacity.");
    }

    [Fact("Disk Calculation: Zero or negative drive size handles cleanly")]
    public void DiskCalculation_InvalidDriveSize_HandlesCleanly()
    {
        var (pct, usedGb, totalGb) = SystemMonitorService.CalculateDiskMetrics(0, 0);

        Assert.Equal(0.0, pct);
        Assert.Equal(0.0, usedGb);
        Assert.Equal(0.0, totalGb);
    }

    // =========================================================================
    // SECTION B: REAL WINDOWS RUNTIME INTEGRATION TESTS
    // These tests query the actual Windows host OS and verify genuine hardware metrics.
    // =========================================================================

    [Fact("Real Windows API: GetSystemTimes returns monotonic tick progression")]
    public void RealWindows_GetSystemTimes_ProducesMonotonicTicks()
    {
        bool success1 = SystemMonitorService.GetSystemTimes(out var i1, out var k1, out var u1);
        Assert.True(success1, "kernel32!GetSystemTimes must succeed on Windows.");

        Thread.Sleep(30);

        bool success2 = SystemMonitorService.GetSystemTimes(out var i2, out var k2, out var u2);
        Assert.True(success2, "Second kernel32!GetSystemTimes call must succeed.");

        ulong total1 = SystemMonitorService.ToUInt64(k1) + SystemMonitorService.ToUInt64(u1);
        ulong total2 = SystemMonitorService.ToUInt64(k2) + SystemMonitorService.ToUInt64(u2);

        Assert.True(total2 >= total1, "Windows system time ticks must progress monotonically.");
    }

    [Fact("Real Windows API: GlobalMemoryStatusEx returns valid physical hardware")]
    public void RealWindows_GlobalMemoryStatusEx_QueriesRealHardware()
    {
        var mem = new SystemMonitorService.MEMORYSTATUSEX();
        bool success = SystemMonitorService.GlobalMemoryStatusEx(mem);
        Assert.True(success, "kernel32!GlobalMemoryStatusEx must succeed on Windows.");

        double totalGb = (double)mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
        double availGb = (double)mem.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);

        Assert.True(totalGb >= 1.0, $"Host machine physical RAM must be at least 1 GB (actual: {totalGb:F1} GB).");
        Assert.True(availGb <= totalGb, "Available physical RAM cannot exceed total physical RAM.");
        Assert.True(mem.dwMemoryLoad >= 1 && mem.dwMemoryLoad <= 100, $"Memory load must be within 1-100% (actual: {mem.dwMemoryLoad}%).");
    }

    [Fact("Real Windows Drive: Primary system volume returns valid storage metrics")]
    public void RealWindows_SystemDrive_QueriesValidVolumeCapacity()
    {
        string sysRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var drive = new DriveInfo(sysRoot);

        Assert.True(drive.IsReady, $"System drive '{sysRoot}' must be ready.");
        Assert.True(drive.TotalSize > 0, $"System drive total size must be > 0 bytes.");
        Assert.True(drive.AvailableFreeSpace > 0, "System drive free space must be > 0 bytes.");
        Assert.True(drive.AvailableFreeSpace <= drive.TotalSize, "Free space cannot exceed total size.");
    }

    [Fact("Real Windows Service: SystemMonitorService generates valid snapshot without fabricated defaults")]
    public void RealWindows_SystemMonitorService_EndToEndSnapshot()
    {
        var service = new SystemMonitorService();
        Thread.Sleep(50); // Allow differential CPU tick
        var snap = service.GetCurrentSnapshot();

        Assert.NotNull(snap);
        Assert.True(snap.IsValid, "Snapshot must be marked valid on genuine Windows host.");
        Assert.True(snap.CpuPercent >= 0.0 && snap.CpuPercent <= 100.0, $"CPU percent {snap.CpuPercent}% must be in [0, 100].");
        Assert.True(snap.RamPercent >= 1.0 && snap.RamPercent <= 100.0, $"RAM percent {snap.RamPercent}% must be in [1, 100].");
        Assert.True(snap.RamTotalGb >= 1.0, $"Total RAM {snap.RamTotalGb} GB must be at least 1 GB.");
        Assert.True(snap.RamUsedGb <= snap.RamTotalGb, $"Used RAM {snap.RamUsedGb} GB must not exceed total RAM {snap.RamTotalGb} GB.");
        Assert.True(snap.DiskTotalGb >= 10.0, $"System disk total {snap.DiskTotalGb} GB must be at least 10 GB.");
        Assert.True(snap.DiskUsedGb <= snap.DiskTotalGb, $"Used disk {snap.DiskUsedGb} GB must not exceed total disk {snap.DiskTotalGb} GB.");
    }

    [Fact("Thread Safety: Concurrent snapshot queries do not cause race conditions or exceptions")]
    public void SystemMonitorService_ThreadSafety_ConcurrentQueries()
    {
        var service = new SystemMonitorService();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, 16, _ =>
        {
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    var snap = service.GetCurrentSnapshot();
                    Assert.NotNull(snap);
                    Assert.True(snap.CpuPercent >= 0.0 && snap.CpuPercent <= 100.0);
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.True(exceptions.IsEmpty, $"Expected no exceptions during concurrent queries, but caught {exceptions.Count}.");
    }

    [Fact("UI Responsiveness: Querying GetCurrentSnapshot executes in under 15ms")]
    public void SystemMonitorService_UIResponsiveness_QueryExecutionTime()
    {
        var service = new SystemMonitorService();

        var sw = Stopwatch.StartNew();
        var snap = service.GetCurrentSnapshot();
        sw.Stop();

        Assert.NotNull(snap);
        Assert.True(sw.ElapsedMilliseconds < 15, $"Snapshot query took {sw.ElapsedMilliseconds}ms, must be < 15ms for UI thread safety.");
    }
}

