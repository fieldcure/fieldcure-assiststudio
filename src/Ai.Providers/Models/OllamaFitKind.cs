namespace FieldCure.Ai.Providers.Models;

/// <summary>
/// Describes how well an Ollama model fits the available hardware.
/// Enum values are ordered best to worst so that <c>OrderBy(x => x.Fit)</c>
/// naturally sorts GPU-first.
/// </summary>
public enum OllamaFitKind
{
    /// <summary>Model fits entirely in VRAM.</summary>
    Gpu,

    /// <summary>Model fits in system RAM (CPU inference).</summary>
    Cpu,

    /// <summary>Model may run via partial offload but could be slow.</summary>
    Maybe,

    /// <summary>Model does not fit in available memory.</summary>
    NoFit
}
