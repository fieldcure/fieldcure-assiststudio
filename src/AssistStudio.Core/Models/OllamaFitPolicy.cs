namespace FieldCure.AssistStudio.Models;

/// <summary>
/// Pure-function policy that classifies whether an Ollama model
/// can run on the given hardware budget.
/// </summary>
public static class OllamaFitPolicy
{
    /// <summary>GPU overhead factor (KV cache, CUDA context, etc.).</summary>
    private const double GpuOverheadFactor = 1.15;

    /// <summary>CPU overhead factor (memory-mapped + runtime overhead).</summary>
    private const double CpuOverheadFactor = 1.35;

    /// <summary>Hybrid split factor (partial offload scenario).</summary>
    private const double HybridOverheadFactor = 1.20;

    /// <summary>Weight applied to RAM contribution in hybrid mode (CPU offload is slower, so RAM is discounted).</summary>
    private const double HybridRamWeight = 0.6;

    /// <summary>
    /// Classifies how well a model fits the available hardware.
    /// </summary>
    /// <returns>A <see cref="OllamaFitKind"/> classification.</returns>
    public static OllamaFitKind Classify(OllamaModelMeta model, HardwareBudget hw)
    {
        // If size metadata is missing, don't hide the model — show as Maybe
        if (model.SizeBytes is null or <= 0)
            return OllamaFitKind.Maybe;

        var size = (double)model.SizeBytes.Value;
        var gpuNeed = size * GpuOverheadFactor;
        var cpuNeed = size * CpuOverheadFactor;
        var hybridNeed = size * HybridOverheadFactor;

        if (hw.AvailableVramBytes >= gpuNeed)
            return OllamaFitKind.Gpu;

        if (hw.AvailableRamBytes >= cpuNeed)
            return OllamaFitKind.Cpu;

        if (hw.AvailableVramBytes + hw.AvailableRamBytes * HybridRamWeight >= hybridNeed)
            return OllamaFitKind.Maybe;

        return OllamaFitKind.NoFit;
    }
}
