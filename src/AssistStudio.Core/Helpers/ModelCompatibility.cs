using System.Runtime.Versioning;

namespace FieldCure.AssistStudio.Helpers;

/// <summary>
/// Indicates how compatible a model is with the current hardware.
/// </summary>
public enum CompatibilityLevel
{
    /// <summary>The model fits comfortably within available VRAM.</summary>
    Compatible,

    /// <summary>The model may run but could be slow due to limited VRAM headroom.</summary>
    NotRecommended,

    /// <summary>The model requires more VRAM than is available.</summary>
    NotCompatible,

    /// <summary>Compatibility could not be determined (e.g., unknown model or VRAM).</summary>
    Unknown
}

/// <summary>
/// Checks whether a local AI model is compatible with the current system's hardware.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ModelCompatibility
{
    #region Constants

    /// <summary>Estimated model sizes in bytes (approximate download/disk sizes for quantized models).</summary>
    private static readonly Dictionary<string, long> EstimatedModelSizes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["llama3.1"] = 4_600_000_000L,         // ~4.6 GB (8B Q4)
        ["llama3.1:70b"] = 40_000_000_000L,    // ~40 GB (70B Q4)
        ["phi4"] = 8_400_000_000L,             // ~8.4 GB (14B Q4)
        ["gemma2"] = 5_400_000_000L,           // ~5.4 GB (9B Q4)
        ["qwen2.5"] = 4_400_000_000L,          // ~4.4 GB (7B Q4)
        ["mistral"] = 4_100_000_000L,          // ~4.1 GB (7B Q4)
        ["deepseek-r1"] = 4_700_000_000L,      // ~4.7 GB (default Q4)
        ["codellama"] = 3_800_000_000L,        // ~3.8 GB (7B Q4)
        ["llava"] = 4_500_000_000L,            // ~4.5 GB (7B Q4)
    };

    /// <summary>Two gigabytes in bytes, used as a comfortable VRAM headroom threshold.</summary>
    private const long TwoGb = 2L * 1024 * 1024 * 1024;

    /// <summary>One gigabyte in bytes, used as a minimum VRAM headroom threshold.</summary>
    private const long OneGb = 1L * 1024 * 1024 * 1024;

    #endregion

    #region Public Methods

    /// <summary>
    /// Checks hardware compatibility for a model of the given size.
    /// </summary>
    public static CompatibilityLevel Check(long modelSizeBytes, HardwareSpec hw)
    {
        if (modelSizeBytes <= 0 || hw.VramBytes <= 0)
            return CompatibilityLevel.Unknown;

        if (hw.VramBytes >= modelSizeBytes + TwoGb)
            return CompatibilityLevel.Compatible;

        if (hw.VramBytes >= modelSizeBytes + OneGb)
            return CompatibilityLevel.NotRecommended;

        return CompatibilityLevel.NotCompatible;
    }

    /// <summary>
    /// Checks hardware compatibility for a model identified by name, using estimated sizes.
    /// </summary>
    public static CompatibilityLevel CheckByModelName(string modelName, HardwareSpec hw)
    {
        var baseName = modelName.Split(':')[0].ToLowerInvariant();
        if (EstimatedModelSizes.TryGetValue(modelName, out var size) ||
            EstimatedModelSizes.TryGetValue(baseName, out size))
        {
            return Check(size, hw);
        }

        return CompatibilityLevel.Unknown;
    }

    /// <summary>
    /// Returns the estimated download size in bytes for a known model name.
    /// </summary>
    public static long GetEstimatedSize(string modelName)
    {
        var baseName = modelName.Split(':')[0].ToLowerInvariant();
        if (EstimatedModelSizes.TryGetValue(modelName, out var size) ||
            EstimatedModelSizes.TryGetValue(baseName, out size))
        {
            return size;
        }

        return 0;
    }

    /// <summary>
    /// Returns a Unicode icon representing the given compatibility level.
    /// </summary>
    public static string GetCompatibilityIcon(CompatibilityLevel level) => level switch
    {
        CompatibilityLevel.Compatible => "\u2705",
        CompatibilityLevel.NotRecommended => "\u26A0\uFE0F",
        CompatibilityLevel.NotCompatible => "\u274C",
        _ => "\u2753"
    };

    /// <summary>
    /// Returns a human-readable text description for the given compatibility level.
    /// </summary>
    public static string GetCompatibilityText(CompatibilityLevel level) => level switch
    {
        CompatibilityLevel.Compatible => "Compatible",
        CompatibilityLevel.NotRecommended => "May be slow",
        CompatibilityLevel.NotCompatible => "Not enough VRAM",
        _ => "Unknown"
    };

    #endregion
}
