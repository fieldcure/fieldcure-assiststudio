using FieldCure.Ai.Execution.Helpers;
using FieldCure.Ai.Execution.Models;

namespace FieldCure.Ai.Execution.Tests;

[TestClass]
public sealed class SystemPromptHintsTests
{
    [TestMethod]
    public void BuildRagHint_ContainsKbId()
    {
        var hint = SystemPromptHints.BuildRagHint("test-kb-123");

        Assert.IsTrue(hint.Contains("kb_id=\"test-kb-123\""));
        Assert.IsTrue(hint.Contains("search_documents"));
        Assert.IsTrue(hint.Contains("get_document_chunk"));
        Assert.IsTrue(hint.Contains("Knowledge Base"));
    }

    [TestMethod]
    public void BuildFromHints_NullHints_ReturnsNull()
    {
        var result = SystemPromptHints.BuildFromHints(null);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void BuildFromHints_EmptyHints_ReturnsNull()
    {
        var result = SystemPromptHints.BuildFromHints(new Dictionary<string, string>());
        Assert.IsNull(result);
    }

    [TestMethod]
    public void BuildFromHints_KbId_ReturnsRagHint()
    {
        var hints = new Dictionary<string, string>
        {
            [ContextHintKeys.KbId] = "abc-def-123"
        };

        var result = SystemPromptHints.BuildFromHints(hints);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Contains("kb_id=\"abc-def-123\""));
    }

    [TestMethod]
    public void BuildFromHints_EmptyKbId_ReturnsNull()
    {
        var hints = new Dictionary<string, string>
        {
            [ContextHintKeys.KbId] = ""
        };

        var result = SystemPromptHints.BuildFromHints(hints);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void BuildFromHints_UnknownKey_ReturnsNull()
    {
        var hints = new Dictionary<string, string>
        {
            ["unknown_key"] = "some_value"
        };

        var result = SystemPromptHints.BuildFromHints(hints);
        Assert.IsNull(result);
    }
}
