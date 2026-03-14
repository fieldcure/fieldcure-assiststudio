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
