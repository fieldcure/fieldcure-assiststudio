using FieldCure.Ai.Providers;

namespace FieldCure.Ai.Providers.Tests;

/// <summary>
/// Tests Gemini's synthesized tool_call_id format and the strip routine that
/// recovers the original function name on send-build.
/// </summary>
[TestClass]
public class GeminiToolCallIdTests
{
    [TestMethod]
    public void Synthesize_ProducesFuncNameWith8HexSuffix()
    {
        var id = GeminiProvider.SynthesizeToolCallId("delegate_task");
        Assert.IsTrue(id.StartsWith("delegate_task_"), $"id={id}");
        var suffix = id["delegate_task_".Length..];
        Assert.AreEqual(8, suffix.Length);
        foreach (var c in suffix)
            Assert.IsTrue(IsHex(c), $"non-hex char in suffix: '{c}'");
    }

    [TestMethod]
    public void Synthesize_TwoCallsProduceDistinctIds()
    {
        var a = GeminiProvider.SynthesizeToolCallId("search");
        var b = GeminiProvider.SynthesizeToolCallId("search");
        Assert.AreNotEqual(a, b, "synthesized ids must be unique across calls");
    }

    [TestMethod]
    public void Strip_RoundTripsSynthesizedId()
    {
        var id = GeminiProvider.SynthesizeToolCallId("delegate_task");
        Assert.AreEqual("delegate_task", GeminiProvider.StripToolCallIdSuffix(id));
    }

    [TestMethod]
    public void Strip_PreservesLegacyBareFuncName()
    {
        // Legacy format from older .astx — single call, no suffix.
        Assert.AreEqual("delegate_task", GeminiProvider.StripToolCallIdSuffix("delegate_task"));
    }

    [TestMethod]
    public void Strip_HandlesLegacyIntSuffix()
    {
        // Legacy format from older .astx — multi-call positional suffix.
        Assert.AreEqual("scan_directory", GeminiProvider.StripToolCallIdSuffix("scan_directory_0"));
        Assert.AreEqual("scan_directory", GeminiProvider.StripToolCallIdSuffix("scan_directory_5"));
        Assert.AreEqual("delegate_task", GeminiProvider.StripToolCallIdSuffix("delegate_task_42"));
    }

    [TestMethod]
    public void Strip_PreservesFuncNameWithUnderscoresAndNonNumericTail()
    {
        // Multi-underscore funcName whose tail is neither hex nor int → keep as-is.
        Assert.AreEqual("fetch_v2", GeminiProvider.StripToolCallIdSuffix("fetch_v2"));
        Assert.AreEqual("get_user_info", GeminiProvider.StripToolCallIdSuffix("get_user_info"));
    }

    [TestMethod]
    public void Strip_HandlesMultiUnderscoreFuncNameWithSyntheticSuffix()
    {
        // Synthetic suffix attached to a multi-underscore funcName.
        var id = GeminiProvider.SynthesizeToolCallId("get_user_info");
        Assert.AreEqual("get_user_info", GeminiProvider.StripToolCallIdSuffix(id));
        // Also: legacy int suffix on multi-underscore funcName.
        Assert.AreEqual("get_user_info", GeminiProvider.StripToolCallIdSuffix("get_user_info_2"));
    }

    [TestMethod]
    public void Strip_DoesNotStripWhenSuffixIsNotIntOrHex8()
    {
        // 7-char hex (not 8) — kept intact.
        Assert.AreEqual("foo_abc1234", GeminiProvider.StripToolCallIdSuffix("foo_abc1234"));
        // 9-char hex — kept intact.
        Assert.AreEqual("foo_abc123456", GeminiProvider.StripToolCallIdSuffix("foo_abc123456"));
        // 8-char with non-hex letter — kept intact.
        Assert.AreEqual("foo_abc12g3h", GeminiProvider.StripToolCallIdSuffix("foo_abc12g3h"));
    }

    [TestMethod]
    public void Strip_HandlesEmptyAndNoUnderscore()
    {
        Assert.AreEqual("", GeminiProvider.StripToolCallIdSuffix(""));
        Assert.AreEqual("simple", GeminiProvider.StripToolCallIdSuffix("simple"));
        // Underscore at position 0 — not stripped (no funcName before it).
        Assert.AreEqual("_5", GeminiProvider.StripToolCallIdSuffix("_5"));
    }

    /// <summary>Hex digit predicate matching the implementation under test.</summary>
    private static bool IsHex(char c)
        => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}
