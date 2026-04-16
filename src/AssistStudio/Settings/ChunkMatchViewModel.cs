namespace AssistStudio.Settings;

/// <summary>
/// Presentation model for a single chunk hit displayed inside a KbCard.
/// </summary>
public sealed record ChunkMatchViewModel(
    string SourceName,
    string Snippet,
    double Score,
    string? ChunkId)
{
    /// <summary>Score formatted for UI display.</summary>
    public string ScoreText => Score.ToString("F2");
}
