using FieldCure.AssistStudio.Controls;

namespace FieldCure.AssistStudio.Core.Tests;

/// <summary>
/// Tests <see cref="ModelPickerEntry"/> equality semantics — selection stability across
/// ItemsSource reassignment depends on identity being (GroupKey, ModelId). See spec §9.1.
/// </summary>
[TestClass]
public class ModelPickerEntryTests
{
    [TestMethod]
    public void Equals_SameGroupSameModelId_AreEqual()
    {
        var a = new ModelPickerEntry { ModelId = "claude-opus-4-7", GroupKey = "Claude" };
        var b = new ModelPickerEntry
        {
            ModelId = "claude-opus-4-7",
            GroupKey = "Claude",
            DisplayName = "Different display",
            Description = "Different desc",
            Tag = new object(),
        };

        Assert.AreEqual(a, b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    [TestMethod]
    public void Equals_DifferentGroup_AreNotEqual()
    {
        var a = new ModelPickerEntry { ModelId = "gpt-5", GroupKey = "OpenAI" };
        var b = new ModelPickerEntry { ModelId = "gpt-5", GroupKey = "Custom_x" };
        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void Equals_DifferentModelId_AreNotEqual()
    {
        var a = new ModelPickerEntry { ModelId = "claude-opus-4-7", GroupKey = "Claude" };
        var b = new ModelPickerEntry { ModelId = "claude-haiku-4-5", GroupKey = "Claude" };
        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void Equals_NullGroup_TreatedAsDistinctFromNonNull()
    {
        var a = new ModelPickerEntry { ModelId = "demo", GroupKey = null };
        var b = new ModelPickerEntry { ModelId = "demo", GroupKey = "Mock" };
        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void Equals_OrdinalCaseSensitive()
    {
        var a = new ModelPickerEntry { ModelId = "Claude-Opus-4-7", GroupKey = "Claude" };
        var b = new ModelPickerEntry { ModelId = "claude-opus-4-7", GroupKey = "Claude" };
        Assert.AreNotEqual(a, b, "ordinal comparison must be case-sensitive");
    }

    [TestMethod]
    public void Equals_NullOther_ReturnsFalse()
    {
        var a = new ModelPickerEntry { ModelId = "demo", GroupKey = "Mock" };
        Assert.IsFalse(a.Equals((ModelPickerEntry?)null));
        Assert.IsFalse(a.Equals((object?)null));
    }

    [TestMethod]
    public void HashCode_StableAcrossReinstantiation()
    {
        var a = new ModelPickerEntry { ModelId = "gpt-5", GroupKey = "OpenAI" };
        var b = new ModelPickerEntry { ModelId = "gpt-5", GroupKey = "OpenAI" };
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }
}
