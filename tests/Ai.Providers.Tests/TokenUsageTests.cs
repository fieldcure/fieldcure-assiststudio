using FieldCure.Ai.Providers.Models;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.Ai.Providers.Tests;

[TestClass]
public class TokenUsageTests
{
    [TestMethod]
    public void TotalTokens_SumsInputAndOutput()
    {
        var usage = new TokenUsage(100, 50);
        Assert.AreEqual(150, usage.TotalTokens);
    }

    [TestMethod]
    public void RecordEquality_SameValues_Equal()
    {
        var a = new TokenUsage(100, 50);
        var b = new TokenUsage(100, 50);
        Assert.AreEqual(a, b);
    }
}
