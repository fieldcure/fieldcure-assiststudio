using FieldCure.Ai.Providers.Models;

namespace FieldCure.Ai.Providers.Tests;

/// <summary>
/// Tests prompt builder composition for base prompts, memory, workspace context, and retrieved chunks.
/// </summary>
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
    public void Build_WorkspaceOnly_AppendsWorkspace()
    {
        var result = PromptBuilder.Build("Base", "Active dataset: X", null);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Contains("[Workspace Context]"));
        Assert.IsTrue(result.Contains("Active dataset: X"));
        Assert.IsTrue(result.StartsWith("Base"));
    }

    [TestMethod]
    public void Build_ChunksOnly_AppendsChunks()
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
        Assert.IsTrue(result.StartsWith("Base"));
    }

    [TestMethod]
    public void Build_WorkspaceAndChunks_CorrectOrder()
    {
        var chunks = new List<ContextChunk> { new("retrieved info", "src") };

        var result = PromptBuilder.Build("Base", "workspace state", chunks);

        Assert.IsNotNull(result);
        var baseIndex = result.IndexOf("Base");
        var wsIndex = result.IndexOf("[Workspace Context]");
        var rcIndex = result.IndexOf("[Retrieved Context]");

        Assert.IsTrue(baseIndex < wsIndex, "Base prompt should come before workspace");
        Assert.IsTrue(wsIndex < rcIndex, "Workspace should come before retrieved context");
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

    [TestMethod]
    public void Build_WithMemory_InsertsAfterBaseBeforeWorkspace()
    {
        var chunks = new List<ContextChunk> { new("rag chunk") };
        var memory = "## User Memory\n- User prefers dark theme.";

        var result = PromptBuilder.Build("Base", "workspace", chunks, memory);

        Assert.IsNotNull(result);
        var baseIndex = result.IndexOf("Base");
        var memIndex = result.IndexOf("## User Memory");
        var wsIndex = result.IndexOf("[Workspace Context]");
        var rcIndex = result.IndexOf("[Retrieved Context]");

        Assert.IsTrue(baseIndex < memIndex, "Base prompt should come before memory");
        Assert.IsTrue(memIndex < wsIndex, "Memory should come before workspace");
        Assert.IsTrue(wsIndex < rcIndex, "Workspace should come before retrieved context");
    }

    [TestMethod]
    public void Build_MemoryOnly_InsertsAfterBase()
    {
        var memory = "## User Memory\n- User is a data scientist.";

        var result = PromptBuilder.Build("Base", null, null, memory);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.StartsWith("Base"));
        Assert.IsTrue(result.Contains("## User Memory"));
        Assert.IsTrue(result.Contains("data scientist"));
    }

    [TestMethod]
    public void Build_NullMemory_NoMemorySection()
    {
        var result = PromptBuilder.Build("Base", null, null, null);
        Assert.AreEqual("Base", result);
    }
}
