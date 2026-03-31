using FieldCure.Ai.Providers.Models;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.Ai.Providers.Tests;

/// <summary>
/// Unit tests for <see cref="OllamaFitPolicy"/>.
/// </summary>
[TestClass]
public class OllamaFitPolicyTests
{
    #region Constants

    /// <summary>One gigabyte in bytes, used as a convenient multiplier in test data.</summary>
    private const long GB = 1024L * 1024 * 1024;

    #endregion

    #region Null / Invalid Size Tests

    /// <summary>Verifies that a model with null size is classified as <see cref="OllamaFitKind.Maybe"/>.</summary>
    [TestMethod]
    public void NullSize_ReturnsMaybe()
    {
        var model = new OllamaModelMeta("test", null, null, null);
        var hw = new HardwareBudget(16 * GB, 8 * GB);
        Assert.AreEqual(OllamaFitKind.Maybe, OllamaFitPolicy.Classify(model, hw));
    }

    /// <summary>Verifies that a model with zero size is classified as <see cref="OllamaFitKind.Maybe"/>.</summary>
    [TestMethod]
    public void ZeroSize_ReturnsMaybe()
    {
        var model = new OllamaModelMeta("test", 0, null, null);
        var hw = new HardwareBudget(16 * GB, 8 * GB);
        Assert.AreEqual(OllamaFitKind.Maybe, OllamaFitPolicy.Classify(model, hw));
    }

    /// <summary>Verifies that a model with negative size is classified as <see cref="OllamaFitKind.Maybe"/>.</summary>
    [TestMethod]
    public void NegativeSize_ReturnsMaybe()
    {
        var model = new OllamaModelMeta("test", -1, null, null);
        var hw = new HardwareBudget(16 * GB, 8 * GB);
        Assert.AreEqual(OllamaFitKind.Maybe, OllamaFitPolicy.Classify(model, hw));
    }

    #endregion

    #region Fit Classification Tests

    /// <summary>Verifies that a small model with large VRAM is classified as <see cref="OllamaFitKind.Gpu"/>.</summary>
    [TestMethod]
    public void SmallModel_LargeVram_ReturnsGpu()
    {
        var model = new OllamaModelMeta("small", 4 * GB, "7B", "Q4_0");
        var hw = new HardwareBudget(32 * GB, 12 * GB);
        Assert.AreEqual(OllamaFitKind.Gpu, OllamaFitPolicy.Classify(model, hw));
    }

    /// <summary>Verifies that a large model with no VRAM but sufficient RAM is classified as <see cref="OllamaFitKind.Cpu"/>.</summary>
    [TestMethod]
    public void LargeModel_NoVram_EnoughRam_ReturnsCpu()
    {
        var model = new OllamaModelMeta("large", 20 * GB, "33B", "Q4_K_M");
        var hw = new HardwareBudget(64 * GB, 0);
        Assert.AreEqual(OllamaFitKind.Cpu, OllamaFitPolicy.Classify(model, hw));
    }

    /// <summary>Verifies that a huge model with insufficient memory is classified as <see cref="OllamaFitKind.NoFit"/>.</summary>
    [TestMethod]
    public void HugeModel_InsufficientMemory_ReturnsNoFit()
    {
        var model = new OllamaModelMeta("huge", 40 * GB, "70B", "Q4_0");
        var hw = new HardwareBudget(16 * GB, 8 * GB);
        Assert.AreEqual(OllamaFitKind.NoFit, OllamaFitPolicy.Classify(model, hw));
    }

    /// <summary>Verifies that a mid-size model with partial VRAM is classified as <see cref="OllamaFitKind.Maybe"/> (hybrid).</summary>
    [TestMethod]
    public void MidModel_PartialVram_ReturnsMaybe()
    {
        var model = new OllamaModelMeta("mid", 10 * GB, "13B", "Q4_K_M");
        var hw = new HardwareBudget(12 * GB, 6 * GB);
        Assert.AreEqual(OllamaFitKind.Maybe, OllamaFitPolicy.Classify(model, hw));
    }

    #endregion

    #region Enum Ordering Tests

    /// <summary>Verifies that enum values sort in the expected order: Gpu, Cpu, Maybe, NoFit.</summary>
    [TestMethod]
    public void OrderBy_FitKind_SortsCorrectly()
    {
        var kinds = new[] { OllamaFitKind.NoFit, OllamaFitKind.Maybe, OllamaFitKind.Gpu, OllamaFitKind.Cpu };
        var sorted = kinds.OrderBy(k => k).ToArray();
        CollectionAssert.AreEqual(
            new[] { OllamaFitKind.Gpu, OllamaFitKind.Cpu, OllamaFitKind.Maybe, OllamaFitKind.NoFit },
            sorted);
    }

    #endregion
}
