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
    double DiskTotalGb)
{
    public static readonly SystemSnapshot Default = new(
        CpuPercent: 8,
        RamPercent: 50,
        RamUsedGb: 8.0,
        RamTotalGb: 16.0,
        DiskPercent: 40,
        DiskUsedGb: 180.0,
        DiskTotalGb: 500.0);
}

public class SystemMonitorService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
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
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    private ulong _lastIdle;
    private ulong _lastKernel;
    private ulong _lastUser;
    private bool _hasPreviousCpuSample;

    public SystemMonitorService()
    {
        SampleCpu();
    }

    private static ulong ToUInt64(FILETIME ft) => ((ulong)ft.dwHighDateTime << 32) | ft.dwLowDateTime;

    private double SampleCpu()
    {
        try
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user))
            {
                return 8.0;
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
                return 8.0;
            }

            ulong diffIdle = idleTime - _lastIdle;
            ulong diffKernel = kernelTime - _lastKernel;
            ulong diffUser = userTime - _lastUser;

            _lastIdle = idleTime;
            _lastKernel = kernelTime;
            _lastUser = userTime;

            ulong totalSys = diffKernel + diffUser;
            if (totalSys == 0) return 8.0;

            double percent = (totalSys - diffIdle) * 100.0 / totalSys;
            return Math.Clamp(Math.Round(percent, 0), 0, 100);
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to sample CPU: {ex.Message}");
            return 8.0;
        }
    }

    public SystemSnapshot GetCurrentSnapshot()
    {
        double cpu = SampleCpu();
        double ramPercent = 50.0;
        double ramUsedGb = 8.0;
        double ramTotalGb = 16.0;

        try
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                ramTotalGb = Math.Round(mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0), 1);
                double availGb = mem.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
                ramUsedGb = Math.Round(ramTotalGb - availGb, 1);
                ramPercent = Math.Clamp(mem.dwMemoryLoad, 0, 100);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to query RAM: {ex.Message}");
        }

        double diskPercent = 40.0;
        double diskUsedGb = 180.0;
        double diskTotalGb = 500.0;

        try
        {
            var sysDrive = DriveInfo.GetDrives()
                .FirstOrDefault(d => d.IsReady && (d.RootDirectory.FullName.StartsWith("C", StringComparison.OrdinalIgnoreCase) || d.DriveType == DriveType.Fixed));

            if (sysDrive != null)
            {
                diskTotalGb = Math.Round(sysDrive.TotalSize / (1024.0 * 1024.0 * 1024.0), 0);
                double freeGb = sysDrive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                diskUsedGb = Math.Round(diskTotalGb - freeGb, 0);
                if (diskTotalGb > 0)
                {
                    diskPercent = Math.Clamp(Math.Round(diskUsedGb * 100.0 / diskTotalGb, 0), 0, 100);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to query Disk: {ex.Message}");
        }

        return new SystemSnapshot(
            CpuPercent: cpu,
            RamPercent: ramPercent,
            RamUsedGb: ramUsedGb,
            RamTotalGb: ramTotalGb,
            DiskPercent: diskPercent,
            DiskUsedGb: diskUsedGb,
            DiskTotalGb: diskTotalGb);
    }
}
