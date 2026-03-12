namespace FluentView.AI.Models;

public partial record TokenUsage(
    int InputTokens,
    int OutputTokens
)
{
    public int TotalTokens => InputTokens + OutputTokens;
}
