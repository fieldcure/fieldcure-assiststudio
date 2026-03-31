using System.Text.Json;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Models;

namespace FieldCure.AssistStudio.Tests;

[TestClass]
public class ProviderPresetTests
{
    [TestMethod]
    [DataRow("Claude", true)]
    [DataRow("OpenAI", true)]
    [DataRow("Gemini", true)]
    [DataRow("Groq", true)]
    [DataRow("Mock", false)]
    [DataRow("Ollama", false)]
    public void RequiresApiKey_ByProviderType(string providerType, bool expected)
    {
        var preset = new ProviderPreset { ProviderType = providerType };
        Assert.AreEqual(expected, preset.RequiresApiKey);
    }

    [TestMethod]
    public void Defaults_AreCorrect()
    {
        var preset = new ProviderPreset();
        Assert.AreEqual(0.7, preset.Temperature);
        Assert.AreEqual(4096, preset.MaxTokens);
        Assert.IsTrue(preset.StreamingEnabled);
        Assert.AreEqual("Mock", preset.ProviderType);
    }

    [TestMethod]
    public void PropertyChanged_RaisedOnNameChange()
    {
        var preset = new ProviderPreset();
        string? changedProperty = null;
        preset.PropertyChanged += (_, e) => changedProperty = e.PropertyName;

        preset.Name = "Test";
        Assert.AreEqual("Name", changedProperty);
    }

}

[TestClass]
public class ProfileTests
{
    [TestMethod]
    public void Defaults_AreCorrect()
    {
        var profile = new Profile();
        Assert.AreEqual("", profile.Name);
        Assert.AreEqual("", profile.SystemPrompt);
        Assert.IsFalse(profile.IsBuiltIn);
        Assert.IsNull(profile.PreferredProviderType);
        Assert.IsNull(profile.PreferredModelId);
        Assert.IsEmpty(profile.ToolNames);
    }

    [TestMethod]
    public void Serialize_IncludesAllFields()
    {
        var profile = new Profile
        {
            Name = "File Organizer",
            SystemPrompt = "You are a file organization assistant.",
            IsBuiltIn = false,
            PreferredProviderType = "Ollama",
            PreferredModelId = "llama3.2",
            ToolNames = ["scan_directory"]
        };

        var json = JsonSerializer.Serialize(profile);
        var deserialized = JsonSerializer.Deserialize<Profile>(json)!;

        Assert.AreEqual("File Organizer", deserialized.Name);
        Assert.AreEqual("You are a file organization assistant.", deserialized.SystemPrompt);
        Assert.IsFalse(deserialized.IsBuiltIn);
        Assert.AreEqual("Ollama", deserialized.PreferredProviderType);
        Assert.AreEqual("llama3.2", deserialized.PreferredModelId);
        Assert.HasCount(1, deserialized.ToolNames);
        Assert.AreEqual("scan_directory", deserialized.ToolNames[0]);
    }

    [TestMethod]
    public void Deserialize_MissingOptionalFields_UsesDefaults()
    {
        var json = """{"Name":"Test","Text":"Hello"}""";
        var profile = JsonSerializer.Deserialize<Profile>(json)!;

        Assert.AreEqual("Test", profile.Name);
        Assert.AreEqual("Hello", profile.SystemPrompt);
        Assert.IsNull(profile.PreferredProviderType);
        Assert.IsNull(profile.PreferredModelId);
        Assert.IsEmpty(profile.ToolNames);
    }
}
