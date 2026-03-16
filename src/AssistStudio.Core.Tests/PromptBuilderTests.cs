using FieldCure.AssistStudio.Models;
using FieldCure.AssistStudio.Providers;

namespace FieldCure.AssistStudio.Tests;

[TestClass]
public class PromptBuilderTests
{
    [TestMethod]
    public void Build_AllNull_ReturnsNull()
    {
        var result = PromptBuilder.Build(null, null, null);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Build_BasePromptOnly_ReturnsBasePrompt()
    {
        var result = PromptBuilder.Build("You are helpful.", null, null);
        Assert.AreEqual("You are helpful.", result);
    }

    [TestMethod]
    public void Build_EmptyChunks_ReturnsBasePrompt()
    {
        var result = PromptBuilder.Build("Base", null, []);
        Assert.AreEqual("Base", result);
    }

    [TestMethod]
    public void Build_WorkspaceOnly_PrependsWorkspace()
    {
        var result = PromptBuilder.Build("Base", "Active dataset: X", null);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.StartsWith("[Workspace Context]"));
        Assert.IsTrue(result.Contains("Active dataset: X"));
        Assert.IsTrue(result.EndsWith("Base"));
    }

    [TestMethod]
    public void Build_ChunksOnly_PrependsChunks()
    {
        var chunks = new List<ContextChunk>
        {
            new("Chunk text 1", "doc.pdf"),
            new("Chunk text 2")
        };

        var result = PromptBuilder.Build("Base", null, chunks);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Contains("[Retrieved Context]"));
        Assert.IsTrue(result.Contains("<source: doc.pdf>"));
        Assert.IsTrue(result.Contains("Chunk text 1"));
        Assert.IsTrue(result.Contains("Chunk text 2"));
        Assert.IsTrue(result.EndsWith("Base"));
    }

    [TestMethod]
    public void Build_WorkspaceAndChunks_CorrectOrder()
    {
        var chunks = new List<ContextChunk> { new("retrieved info", "src") };

        var result = PromptBuilder.Build("Base", "workspace state", chunks);

        Assert.IsNotNull(result);
        var wsIndex = result.IndexOf("[Workspace Context]");
        var rcIndex = result.IndexOf("[Retrieved Context]");
        var baseIndex = result.IndexOf("Base");

        Assert.IsTrue(wsIndex < rcIndex, "Workspace should come before retrieved context");
        Assert.IsTrue(rcIndex < baseIndex, "Retrieved context should come before base prompt");
    }

    [TestMethod]
    public void Build_NullBaseWithContext_ReturnsContextOnly()
    {
        var chunks = new List<ContextChunk> { new("some context") };

        var result = PromptBuilder.Build(null, "workspace", chunks);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Contains("[Workspace Context]"));
        Assert.IsTrue(result.Contains("some context"));
    }

    [TestMethod]
    public void Build_ChunkWithoutSource_OmitsSourceTag()
    {
        var chunks = new List<ContextChunk> { new("text only") };

        var result = PromptBuilder.Build("Base", null, chunks);

        Assert.IsNotNull(result);
        Assert.IsFalse(result.Contains("<source:"));
        Assert.IsTrue(result.Contains("text only"));
    }
}
