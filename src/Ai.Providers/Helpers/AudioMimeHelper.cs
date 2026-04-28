using FieldCure.Ai.Providers.Models;

namespace FieldCure.Ai.Providers.Helpers;

/// <summary>
/// Maps audio file extensions to MIME types and exposes per-provider acceptance lists.
/// Used by ComposeBar (compose-time whitelist) and provider serializers (send-time validation).
/// </summary>
public static class AudioMimeHelper
{
    /// <summary>
    /// Audio extensions accepted by ComposeBar input channels (file picker, paste, drag-drop).
    /// Aligns with FieldCure.DocumentParsers.Audio supported set; per-provider gating happens at send time.
    /// </summary>
    public static readonly IReadOnlySet<string> AcceptedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".m4a", ".ogg", ".flac", ".webm"
    };

    /// <summary>
    /// MIME types accepted by Gemini 1.5+ inline_data audio input.
    /// Reference: https://ai.google.dev/gemini-api/docs/audio
    /// </summary>
    public static readonly IReadOnlySet<string> GeminiSupportedMimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "audio/wav", "audio/mp3", "audio/mpeg", "audio/aiff", "audio/aac", "audio/ogg", "audio/flac"
    };

    /// <summary>
    /// MIME types accepted by OpenAI gpt-4o-audio chat completions input_audio.
    /// Only "wav" and "mp3" are documented; others (flac/opus/pcm16) are output-only.
    /// </summary>
    public static readonly IReadOnlySet<string> OpenAiSupportedMimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "audio/wav", "audio/mp3", "audio/mpeg"
    };

    /// <summary>
    /// Gemini inline payload size limit (bytes). Combined request size including text must stay under this.
    /// </summary>
    public const long GeminiInlineSizeLimit = 20L * 1024 * 1024;

    /// <summary>
    /// Returns the MIME type for the given audio extension, or <c>null</c> if not a recognized audio extension.
    /// </summary>
    public static string? GetMimeType(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return null;
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        return ext.ToLowerInvariant() switch
        {
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".m4a" => "audio/mp4",
            ".ogg" => "audio/ogg",
            ".flac" => "audio/flac",
            ".webm" => "audio/webm",
            ".aac" => "audio/aac",
            ".aiff" or ".aif" => "audio/aiff",
            _ => null
        };
    }

    /// <summary>
    /// Returns true if the given extension (with or without leading dot) is in the ComposeBar audio whitelist.
    /// </summary>
    public static bool IsAcceptedExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return false;
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        return AcceptedExtensions.Contains(ext);
    }

    /// <summary>
    /// Returns the supported MIME set for the given audio capability + provider name combination.
    /// </summary>
    public static IReadOnlySet<string>? GetSupportedMimes(string providerName, AudioCapability capability)
    {
        if (capability != AudioCapability.NativeAudio) return null;
        return providerName switch
        {
            "Gemini" => GeminiSupportedMimes,
            "OpenAI" => OpenAiSupportedMimes,
            _ => null
        };
    }
}
