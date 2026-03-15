using FieldCure.AssistStudio.Models;

namespace FieldCure.AssistStudio.Tests;

[TestClass]
public class AiResponseTests
{
    [TestMethod]
    public void HasToolCalls_ReturnsFalse_WhenNoToolCalls()
    {
        var response = new AiResponse { Content = "Hello" };
        Assert.IsFalse(response.HasToolCalls);
    }

    [TestMethod]
    public void HasToolCalls_ReturnsTrue_WhenToolCallsExist()
    {
        var response = new AiResponse
        {
            ToolCalls =
            [
                new ToolCall { Id = "1", FunctionName = "test", Arguments = "{}" }
            ]
        };
        Assert.IsTrue(response.HasToolCalls);
    }

    [TestMethod]
    public void TextOnly_Response_HasContentAndNoToolCalls()
    {
        var response = new AiResponse
        {
            Content = "Some text",
            Usage = new TokenUsage(10, 20),
            IsTruncated = false
        };

        Assert.AreEqual("Some text", response.Content);
        Assert.AreEqual(0, response.ToolCalls.Count);
        Assert.IsFalse(response.HasToolCalls);
        Assert.AreEqual(30, response.Usage!.TotalTokens);
    }
}
