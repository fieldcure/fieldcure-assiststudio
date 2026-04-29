using FieldCure.Ai.Providers.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FieldCure.Ai.Providers.Tests;

/// <summary>
/// Tests <see cref="ProviderModelBroadcast.Apply"/> — the per-Provider broadcast invariant
/// applied by <c>AppSettings.SaveModels</c> before persistence. See spec §9.1.
/// </summary>
[TestClass]
public class ProviderModelBroadcastTests
{
    [TestMethod]
    public void Apply_BroadcastsMaxTokensAcrossSameProvider()
    {
        var models = new List<ProviderModel>
        {
            new() { Name = "claude-opus-4-7", ProviderType = "Claude", ModelId = "claude-opus-4-7", MaxTokens = 8192 },
            new() { Name = "claude-sonnet-4-6", ProviderType = "Claude", ModelId = "claude-sonnet-4-6", MaxTokens = 1024 },
            new() { Name = "gpt-5", ProviderType = "OpenAI", ModelId = "gpt-5", MaxTokens = 2048 },
        };

        ProviderModelBroadcast.Apply(models);

        Assert.AreEqual(8192, models[0].MaxTokens, "first Claude entry is the source");
        Assert.AreEqual(8192, models[1].MaxTokens, "sibling Claude entry is forced to match");
        Assert.AreEqual(2048, models[2].MaxTokens, "OpenAI entry untouched");
    }

    [TestMethod]
    public void Apply_BroadcastsThinkingFields()
    {
        var models = new List<ProviderModel>
        {
            new()
            {
                Name = "claude-opus-4-7", ProviderType = "Claude", ModelId = "claude-opus-4-7",
                ThinkingEnabled = true, ThinkingOverride = ThinkingOverride.ForceOn, ThinkingBudget = 16000,
            },
            new() { Name = "claude-haiku-4-5", ProviderType = "Claude", ModelId = "claude-haiku-4-5" },
        };

        ProviderModelBroadcast.Apply(models);

        Assert.IsTrue(models[1].ThinkingEnabled);
        Assert.AreEqual(ThinkingOverride.ForceOn, models[1].ThinkingOverride);
        Assert.AreEqual(16000, models[1].ThinkingBudget);
    }

    [TestMethod]
    public void Apply_PreservesPerModelKeepAliveAndNumCtx()
    {
        var models = new List<ProviderModel>
        {
            new()
            {
                Name = "llama3.3", ProviderType = "Ollama", ModelId = "llama3.3",
                MaxTokens = 4096, KeepAlive = "1h", NumCtx = 32768,
            },
            new()
            {
                Name = "qwen3:8b", ProviderType = "Ollama", ModelId = "qwen3:8b",
                MaxTokens = 1024, KeepAlive = "0", NumCtx = 8192,
            },
        };

        ProviderModelBroadcast.Apply(models);

        Assert.AreEqual(4096, models[1].MaxTokens, "broadcast field forced to source");
        Assert.AreEqual("0", models[1].KeepAlive, "per-model KeepAlive preserved");
        Assert.AreEqual(8192, models[1].NumCtx, "per-model NumCtx preserved");
    }

    [TestMethod]
    public void Apply_BroadcastsBaseUrl()
    {
        var models = new List<ProviderModel>
        {
            new() { Name = "gpt-5", ProviderType = "Custom_x", ModelId = "gpt-5", BaseUrl = "https://api.example.com/v1" },
            new() { Name = "gpt-4", ProviderType = "Custom_x", ModelId = "gpt-4", BaseUrl = null },
        };

        ProviderModelBroadcast.Apply(models);

        Assert.AreEqual("https://api.example.com/v1", models[1].BaseUrl);
    }

    [TestMethod]
    public void Apply_IsIdempotent()
    {
        var models = new List<ProviderModel>
        {
            new() { Name = "claude-opus-4-7", ProviderType = "Claude", ModelId = "claude-opus-4-7", MaxTokens = 8192 },
            new() { Name = "claude-sonnet-4-6", ProviderType = "Claude", ModelId = "claude-sonnet-4-6", MaxTokens = 8192 },
        };

        ProviderModelBroadcast.Apply(models);
        var snapshot = (models[0].MaxTokens, models[1].MaxTokens, models[0].ThinkingBudget, models[1].ThinkingBudget);
        ProviderModelBroadcast.Apply(models);

        Assert.AreEqual(snapshot, (models[0].MaxTokens, models[1].MaxTokens, models[0].ThinkingBudget, models[1].ThinkingBudget));
    }

    [TestMethod]
    public void Apply_HandlesEmptyAndSingletonInput()
    {
        ProviderModelBroadcast.Apply([]);  // no-op, no exception

        var single = new List<ProviderModel> { new() { ProviderType = "Mock", ModelId = "demo", MaxTokens = 1024 } };
        ProviderModelBroadcast.Apply(single);
        Assert.AreEqual(1024, single[0].MaxTokens);
    }
}
