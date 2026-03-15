using System.Text.Json;
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

    [TestMethod]
    public void Defaults_PromptPresetNameAndToolNames()
    {
        var preset = new ProviderPreset();
        Assert.IsNull(preset.PromptPresetName);
        Assert.AreEqual(0, preset.ToolNames.Count);
    }

    [TestMethod]
    public void Serialize_IncludesPromptPresetNameAndToolNames()
    {
        var preset = new ProviderPreset
        {
            Name = "File Organizer",
            ProviderType = "Ollama",
            ModelId = "llama3.1",
            PromptPresetName = "File Organizer",
            ToolNames = ["scan_directory"]
        };

        var json = JsonSerializer.Serialize(preset);
        var deserialized = JsonSerializer.Deserialize<ProviderPreset>(json)!;

        Assert.AreEqual("File Organizer", deserialized.PromptPresetName);
        Assert.AreEqual(1, deserialized.ToolNames.Count);
        Assert.AreEqual("scan_directory", deserialized.ToolNames[0]);
    }

    [TestMethod]
    public void Deserialize_MissingPromptPresetNameAndToolNames_UsesDefaults()
    {
        var json = """{"Name":"Test","ProviderType":"Mock","ModelId":"test"}""";
        var preset = JsonSerializer.Deserialize<ProviderPreset>(json)!;

        Assert.IsNull(preset.PromptPresetName);
        Assert.AreEqual(0, preset.ToolNames.Count);
    }
}
