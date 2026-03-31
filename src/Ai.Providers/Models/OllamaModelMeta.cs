namespace FieldCure.Ai.Providers.Models;

/// <summary>
/// Lightweight metadata extracted from an Ollama model entry,
/// used by <see cref="OllamaFitPolicy"/> for fit classification.
/// </summary>
/// <param name="Id">Model identifier (e.g. <c>"llama3:8b"</c>).</param>
/// <param name="SizeBytes">On-disk model size in bytes (from <c>/api/tags</c> <c>size</c> field).</param>
/// <param name="ParameterSize">Human-readable parameter count (e.g. <c>"8B"</c>). Informational only.</param>
/// <param name="QuantizationLevel">Quantization label (e.g. <c>"Q4_K_M"</c>). Informational only.</param>
public sealed record OllamaModelMeta(
    string Id,
    long? SizeBytes,
    string? ParameterSize,
    string? QuantizationLevel
);
