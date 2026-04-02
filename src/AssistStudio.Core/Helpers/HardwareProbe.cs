using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FieldCure.AssistStudio.Models;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.AssistStudio.Helpers;

/// <summary>
/// Probes available system RAM and GPU VRAM for model fit classification.
/// </summary>
[SupportedOSPlatform("windows")]
public static class HardwareProbe
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

    #region Public Methods

    /// <summary>
    /// Returns a snapshot of available memory resources.
    /// If VRAM detection fails, <see cref="HardwareBudget.AvailableVramBytes"/> is 0
    /// (policy will fall back to CPU-only classification).
    /// </summary>
    public static Task<HardwareBudget> GetAsync()
    {
        return Task.Run(() =>
        {
            var availableRam = DetectAvailableRam();
            var vram = DetectVram();
            return new HardwareBudget(availableRam, vram);
        });
    }

    #endregion

    #region Private Methods

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

    #endregion
}
