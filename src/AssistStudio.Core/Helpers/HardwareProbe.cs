using System.Runtime.InteropServices;
using FieldCure.AssistStudio.Models;

namespace FieldCure.AssistStudio.Helpers;

/// <summary>
/// Probes available system RAM and GPU VRAM for model fit classification.
/// </summary>
public static class HardwareProbe
{
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    /// <summary>
    /// Returns a snapshot of available memory resources.
    /// If VRAM detection fails, <see cref="HardwareBudget.AvailableVramBytes"/> is 0
    /// (policy will fall back to CPU-only classification).
    /// </summary>
    public static Task<HardwareBudget> GetAsync()
    {
        var availableRam = DetectAvailableRam();
        var vram = DetectVram();

        return Task.FromResult(new HardwareBudget(availableRam, vram));
    }

    /// <summary>
    /// Detects available (free) system RAM in bytes.
    /// </summary>
    private static long DetectAvailableRam()
    {
        try
        {
            var memInfo = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref memInfo))
                return (long)memInfo.ullAvailPhys;
        }
        catch
        {
            // Fallback below
        }

        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }

    /// <summary>
    /// Detects total GPU VRAM in bytes using <see cref="HardwareInfo"/>.
    /// Returns 0 if detection fails.
    /// </summary>
    private static long DetectVram()
    {
        try
        {
            var spec = HardwareInfo.Detect();
            return spec.VramBytes;
        }
        catch
        {
            return 0;
        }
    }
}
