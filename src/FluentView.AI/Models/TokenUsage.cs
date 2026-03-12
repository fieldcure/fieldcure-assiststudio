namespace FluentView.AI.Models;

public record TokenUsage(
    int InputTokens,
    int OutputTokens
)
{
    public int TotalTokens => InputTokens + OutputTokens;
}
