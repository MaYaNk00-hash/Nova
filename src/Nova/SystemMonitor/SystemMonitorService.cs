using System;
using System.IO;
using System.Runtime.InteropServices;
using Nova.Core;

namespace Nova.SystemMonitor;

public record SystemSnapshot(
    double CpuPercent,
    double RamPercent,
    double RamUsedGb,
    double RamTotalGb,
    double DiskPercent,
    double DiskUsedGb,
    double DiskTotalGb,
    bool IsValid = true)
{
    public static readonly SystemSnapshot Default = new(
        CpuPercent: 0,
        RamPercent: 0,
        RamUsedGb: 0.0,
        RamTotalGb: 0.0,
        DiskPercent: 0,
        DiskUsedGb: 0.0,
        DiskTotalGb: 0.0,
        IsValid: false);
}

public class SystemMonitorService
{
    [StructLayout(LayoutKind.Sequential)]
    public struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    private readonly object _syncLock = new();
    private ulong _lastIdle;
    private ulong _lastKernel;
    private ulong _lastUser;
    private bool _hasPreviousCpuSample;
    private double _lastValidCpu = 0.0;
    private (double percent, double usedGb, double totalGb)? _lastValidRam;
    private (double percent, double usedGb, double totalGb)? _lastValidDisk;

    public SystemMonitorService()
    {
        lock (_syncLock)
        {
            SampleCpuInternal();
        }
    }

    public static ulong ToUInt64(FILETIME ft) => ((ulong)ft.dwHighDateTime << 32) | ft.dwLowDateTime;

    /// <summary>
    /// Pure mathematical calculation of CPU utilization percentage from time differentials.
    /// In Windows GetSystemTimes, lpKernelTime ALREADY includes lpIdleTime.
    /// Therefore total system time = (diffKernel + diffUser).
    /// </summary>
    public static double CalculateCpuPercent(ulong prevIdle, ulong prevKernel, ulong prevUser, ulong currentIdle, ulong currentKernel, ulong currentUser)
    {
        ulong diffIdle = currentIdle >= prevIdle ? currentIdle - prevIdle : 0;
        ulong diffKernel = currentKernel >= prevKernel ? currentKernel - prevKernel : 0;
        ulong diffUser = currentUser >= prevUser ? currentUser - prevUser : 0;

        ulong totalSys = diffKernel + diffUser;
        if (totalSys == 0) return 0.0;

        // Guard against unsigned underflow when diffIdle > totalSys due to multi-core timer jitter
        if (diffIdle >= totalSys) return 0.0;

        double percent = (double)(totalSys - diffIdle) * 100.0 / (double)totalSys;
        return Math.Clamp(Math.Round(percent, 0), 0.0, 100.0);
    }

    /// <summary>
    /// Pure calculation of RAM metrics from raw byte values.
    /// </summary>
    public static (double percent, double usedGb, double totalGb) CalculateRamMetrics(ulong totalPhysBytes, ulong availPhysBytes, uint memoryLoad)
    {
        if (totalPhysBytes == 0) return (0.0, 0.0, 0.0);

        double totalGb = Math.Round((double)totalPhysBytes / (1024.0 * 1024.0 * 1024.0), 1);
        double availGb = (double)availPhysBytes / (1024.0 * 1024.0 * 1024.0);
        double usedGb = Math.Round(Math.Max(0.0, totalGb - availGb), 1);
        double percent = Math.Clamp((double)memoryLoad, 0.0, 100.0);

        return (percent, usedGb, totalGb);
    }

    /// <summary>
    /// Pure calculation of Disk capacity metrics from raw byte values.
    /// Measures storage capacity and volume utilization, not I/O transfer rate.
    /// </summary>
    public static (double percent, double usedGb, double totalGb) CalculateDiskMetrics(long totalSizeBytes, long availableFreeSpaceBytes)
    {
        if (totalSizeBytes <= 0) return (0.0, 0.0, 0.0);

        double totalGb = Math.Round((double)totalSizeBytes / (1024.0 * 1024.0 * 1024.0), 0);
        double freeGb = (double)availableFreeSpaceBytes / (1024.0 * 1024.0 * 1024.0);
        double usedGb = Math.Round(Math.Max(0.0, totalGb - freeGb), 0);
        double percent = totalGb > 0 ? Math.Clamp(Math.Round(usedGb * 100.0 / totalGb, 0), 0.0, 100.0) : 0.0;

        return (percent, usedGb, totalGb);
    }

    private double SampleCpuInternal()
    {
        try
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user))
            {
                return _lastValidCpu;
            }

            ulong idleTime = ToUInt64(idle);
            ulong kernelTime = ToUInt64(kernel);
            ulong userTime = ToUInt64(user);

            if (!_hasPreviousCpuSample)
            {
                _lastIdle = idleTime;
                _lastKernel = kernelTime;
                _lastUser = userTime;
                _hasPreviousCpuSample = true;
                return _lastValidCpu;
            }

            ulong diffKernel = kernelTime >= _lastKernel ? kernelTime - _lastKernel : 0;
            ulong diffUser = userTime >= _lastUser ? userTime - _lastUser : 0;
            ulong totalSys = diffKernel + diffUser;

            // If sampled in rapid succession (< 1ms), retain last valid sample
            if (totalSys == 0)
            {
                return _lastValidCpu;
            }

            double percent = CalculateCpuPercent(_lastIdle, _lastKernel, _lastUser, idleTime, kernelTime, userTime);

            _lastIdle = idleTime;
            _lastKernel = kernelTime;
            _lastUser = userTime;
            _lastValidCpu = percent;

            return percent;
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to sample CPU: {ex.Message}");
            return _lastValidCpu;
        }
    }

    public double SampleCpu()
    {
        lock (_syncLock)
        {
            return SampleCpuInternal();
        }
    }

    public SystemSnapshot GetCurrentSnapshot()
    {
        double cpu;
        lock (_syncLock)
        {
            cpu = SampleCpuInternal();
        }

        double ramPercent = 0.0;
        double ramUsedGb = 0.0;
        double ramTotalGb = 0.0;

        try
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                var (pct, used, tot) = CalculateRamMetrics(mem.ullTotalPhys, mem.ullAvailPhys, mem.dwMemoryLoad);
                ramPercent = pct;
                ramUsedGb = used;
                ramTotalGb = tot;
                _lastValidRam = (pct, used, tot);
            }
            else if (_lastValidRam.HasValue)
            {
                (ramPercent, ramUsedGb, ramTotalGb) = _lastValidRam.Value;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to query RAM: {ex.Message}");
            if (_lastValidRam.HasValue)
            {
                (ramPercent, ramUsedGb, ramTotalGb) = _lastValidRam.Value;
            }
        }

        double diskPercent = 0.0;
        double diskUsedGb = 0.0;
        double diskTotalGb = 0.0;

        try
        {
            // Specifically target the primary Windows system drive to avoid hanging on slow/unreachable network mounts
            string sysRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var sysDrive = new DriveInfo(sysRoot);

            if (sysDrive.IsReady)
            {
                var (pct, used, tot) = CalculateDiskMetrics(sysDrive.TotalSize, sysDrive.AvailableFreeSpace);
                diskPercent = pct;
                diskUsedGb = used;
                diskTotalGb = tot;
                _lastValidDisk = (pct, used, tot);
            }
            else if (_lastValidDisk.HasValue)
            {
                (diskPercent, diskUsedGb, diskTotalGb) = _lastValidDisk.Value;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to query Disk: {ex.Message}");
            if (_lastValidDisk.HasValue)
            {
                (diskPercent, diskUsedGb, diskTotalGb) = _lastValidDisk.Value;
            }
        }

        bool isValid = ramTotalGb > 0 && diskTotalGb > 0;

        return new SystemSnapshot(
            CpuPercent: cpu,
            RamPercent: ramPercent,
            RamUsedGb: ramUsedGb,
            RamTotalGb: ramTotalGb,
            DiskPercent: diskPercent,
            DiskUsedGb: diskUsedGb,
            DiskTotalGb: diskTotalGb,
            IsValid: isValid);
    }
}
