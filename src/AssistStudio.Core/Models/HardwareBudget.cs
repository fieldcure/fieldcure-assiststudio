namespace FieldCure.AssistStudio.Models;

/// <summary>
/// Snapshot of available memory resources at a point in time.
/// Obtain via <see cref="Helpers.HardwareProbe.GetAsync"/>.
/// </summary>
/// <param name="AvailableRamBytes">Free (or available) system RAM in bytes.</param>
/// <param name="AvailableVramBytes">Free GPU VRAM in bytes. 0 if detection fails.</param>
public sealed record HardwareBudget(
    long AvailableRamBytes,
    long AvailableVramBytes
);
