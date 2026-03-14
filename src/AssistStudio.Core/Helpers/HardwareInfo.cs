using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace FieldCure.AssistStudio.Helpers;

/// <summary>
/// Contains detected hardware specifications for the current system.
/// </summary>
/// <param name="GpuName">The name of the primary GPU.</param>
/// <param name="VramBytes">The total VRAM in bytes.</param>
/// <param name="TotalRamBytes">The total system RAM in bytes.</param>
/// <param name="OsDisplay">A human-readable OS version string.</param>
[SupportedOSPlatform("windows")]
public record HardwareSpec(
    string GpuName,
    long VramBytes,
    long TotalRamBytes,
    string OsDisplay
)
{
    #region Computed Properties

    /// <summary>A human-readable display string for the VRAM amount (e.g., "8.0 GB").</summary>
    public string VramDisplay => FormatBytes(VramBytes);

    /// <summary>A human-readable display string for the total RAM amount (e.g., "32.0 GB").</summary>
    public string RamDisplay => FormatBytes(TotalRamBytes);

    #endregion

    #region Private Methods

    /// <summary>Formats a byte count as a human-readable GB string.</summary>
    private static string FormatBytes(long bytes)
    {
        const double gb = 1024.0 * 1024 * 1024;
        return $"{bytes / gb:F1} GB";
    }

    #endregion
}

/// <summary>
/// Detects hardware specifications including GPU, VRAM, RAM, and OS version on Windows.
/// </summary>
[SupportedOSPlatform("windows")]
public static class HardwareInfo
{
    #region Native Interop

    /// <summary>Native memory status structure for the GlobalMemoryStatusEx API.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    /// <summary>Retrieves information about the system's current usage of both physical and virtual memory.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    #endregion

    #region Constants

    /// <summary>Registry path for display adapter device class.</summary>
    private const string DisplayClassKey = @"SYSTEM\ControlSet001\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    #endregion

    #region Public Methods

    /// <summary>
    /// Detects and returns the current system's hardware specifications.
    /// </summary>
    public static HardwareSpec Detect()
    {
        var (gpuName, vram) = DetectGpu();
        var ram = DetectRam();
        var os = DetectOs();
        return new HardwareSpec(gpuName, vram, ram, os);
    }

    #endregion

    #region Private Methods

    /// <summary>Detects the primary GPU name and VRAM, trying registry first then WMI fallback.</summary>
    private static (string Name, long VramBytes) DetectGpu()
    {
        // Try registry first for accurate 64-bit VRAM
        var (regName, regVram) = DetectGpuViaRegistry();
        if (regVram > 0)
            return (regName, regVram);

        // Fallback to WMI (AdapterRAM is uint32, capped at ~4GB)
        return DetectGpuViaWmi();
    }

    /// <summary>Detects GPU information via the Windows registry (supports 64-bit VRAM values).</summary>
    private static (string Name, long VramBytes) DetectGpuViaRegistry()
    {
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
            if (classKey is null)
                return ("Unknown GPU", 0);

            var bestName = "Unknown GPU";
            long bestVram = 0;

            foreach (var subKeyName in classKey.GetSubKeyNames())
            {
                // Only numeric subkeys (0000, 0001, etc.)
                if (!int.TryParse(subKeyName, out _))
                    continue;

                using var subKey = classKey.OpenSubKey(subKeyName);
                if (subKey is null)
                    continue;

                var name = subKey.GetValue("DriverDesc") as string;
                if (string.IsNullOrEmpty(name))
                    continue;

                // qwMemorySize is a REG_QWORD (64-bit) — accurate for modern GPUs
                var memObj = subKey.GetValue("HardwareInformation.qwMemorySize");
                if (memObj is long mem && mem > bestVram)
                {
                    bestVram = mem;
                    bestName = name;
                }
            }

            return (bestName, bestVram);
        }
        catch
        {
            return ("Unknown GPU", 0);
        }
    }

    /// <summary>Detects GPU information via WMI (fallback, limited to 4 GB due to uint32 AdapterRAM).</summary>
    private static (string Name, long VramBytes) DetectGpuViaWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM, Status FROM Win32_VideoController");
            using var collection = searcher.Get();

            var bestName = "Unknown GPU";
            long bestVram = 0;

            foreach (var obj in collection)
            {
                using var mo = (ManagementObject)obj;
                var name = mo["Name"]?.ToString();
                var status = mo["Status"]?.ToString();

                if (string.IsNullOrEmpty(name) || status != "OK")
                    continue;

                var adapterRam = Convert.ToInt64(mo["AdapterRAM"] ?? 0);
                if (adapterRam > bestVram)
                {
                    bestVram = adapterRam;
                    bestName = name;
                }
            }

            return (bestName, bestVram);
        }
        catch
        {
            return ("Unknown GPU", 0);
        }
    }

    /// <summary>Detects the Windows OS version and architecture.</summary>
    private static string DetectOs()
    {
        var build = Environment.OSVersion.Version.Build;
        var arch = Environment.Is64BitOperatingSystem ? "x64" : "x86";
        var winVersion = build >= 22000 ? "11" : "10";
        return $"Windows {winVersion} (Build {build}, {arch})";
    }

    /// <summary>Detects total physical RAM in bytes using native API with GC fallback.</summary>
    private static long DetectRam()
    {
        try
        {
            var memInfo = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref memInfo))
                return (long)memInfo.ullTotalPhys;
        }
        catch { }

        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }

    #endregion
}
