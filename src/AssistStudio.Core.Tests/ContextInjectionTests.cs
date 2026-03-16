using System.Text.Json;
using FieldCure.AssistStudio.Models;
using FieldCure.AssistStudio.Providers;

namespace FieldCure.AssistStudio.Tests;

[TestClass]
public class ContextInjectionTests
{
    /// <summary>
    /// Verifies that ContextChunks on AiRequest are prepended to the system prompt
    /// in the actual JSON request body sent by a provider.
    /// </summary>
    [TestMethod]
    public async Task ContextChunks_AppearInProviderRequestBody()
    {
        // Arrange: provider with a fake endpoint (will fail on HTTP, but LastRequestBody is set first)
        using var provider = new OpenAiProvider("fake-key", "gpt-4o", "http://localhost:1");

        var request = new AiRequest
        {
            Messages = [new ChatMessage(ChatRole.User, "What is RAG?")],
            SystemPrompt = "You are helpful.",
            ContextChunks =
            [
                new ContextChunk("Retrieval-augmented generation combines search with LLMs.", "rag-paper.pdf", 0.95),
                new ContextChunk("Vector databases store embeddings for similarity search.")
            ],
            WorkspaceText = "Active dataset: EIS-Nyquist-001"
        };

        // Act: call will throw due to fake endpoint, but LastRequestBody is set beforehand
        try { await provider.CompleteAsync(request); } catch { /* expected */ }

        // Assert: parse the request body and check the system message content
        Assert.IsNotNull(provider.LastRequestBody, "LastRequestBody should be set before HTTP call");

        var doc = JsonDocument.Parse(provider.LastRequestBody);
        var messages = doc.RootElement.GetProperty("messages");
        var systemMsg = messages[0];

        Assert.AreEqual("system", systemMsg.GetProperty("role").GetString());

        var content = systemMsg.GetProperty("content").GetString()!;

        // Workspace context should be present
        Assert.IsTrue(content.Contains("Active dataset: EIS-Nyquist-001"),
            "Workspace text should be in system prompt");

        // RAG chunks should be present
        Assert.IsTrue(content.Contains("Retrieval-augmented generation"),
            "Context chunk text should be in system prompt");
        Assert.IsTrue(content.Contains("<source: rag-paper.pdf>"),
            "Context chunk source should be tagged");
        Assert.IsTrue(content.Contains("Vector databases"),
            "Second chunk should be in system prompt");

        // Original system prompt should still be there
        Assert.IsTrue(content.Contains("You are helpful."),
            "Base system prompt should be preserved");
    }
}
