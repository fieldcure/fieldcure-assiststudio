using FieldCure.AssistStudio.Models;

namespace FieldCure.AssistStudio.Tests;

/// <summary>
/// Unit tests for <see cref="OllamaFitPolicy"/>.
/// </summary>
[TestClass]
public class OllamaFitPolicyTests
{
    private const long GB = 1024L * 1024 * 1024;

    [TestMethod]
    public void NullSize_ReturnsMaybe()
    {
        var model = new OllamaModelMeta("test", null, null, null);
        var hw = new HardwareBudget(16 * GB, 8 * GB);
        Assert.AreEqual(OllamaFitKind.Maybe, OllamaFitPolicy.Classify(model, hw));
    }

    [TestMethod]
    public void ZeroSize_ReturnsMaybe()
    {
        var model = new OllamaModelMeta("test", 0, null, null);
        var hw = new HardwareBudget(16 * GB, 8 * GB);
        Assert.AreEqual(OllamaFitKind.Maybe, OllamaFitPolicy.Classify(model, hw));
    }

    [TestMethod]
    public void SmallModel_LargeVram_ReturnsGpu()
    {
        // 4 GB model, 12 GB VRAM
        var model = new OllamaModelMeta("small", 4 * GB, "7B", "Q4_0");
        var hw = new HardwareBudget(32 * GB, 12 * GB);
        Assert.AreEqual(OllamaFitKind.Gpu, OllamaFitPolicy.Classify(model, hw));
    }

    [TestMethod]
    public void LargeModel_NoVram_EnoughRam_ReturnsCpu()
    {
        // 20 GB model, 0 VRAM, 64 GB RAM
        var model = new OllamaModelMeta("large", 20 * GB, "33B", "Q4_K_M");
        var hw = new HardwareBudget(64 * GB, 0);
        Assert.AreEqual(OllamaFitKind.Cpu, OllamaFitPolicy.Classify(model, hw));
    }

    [TestMethod]
    public void HugeModel_InsufficientMemory_ReturnsNoFit()
    {
        // 40 GB model, 8 GB VRAM, 16 GB RAM
        var model = new OllamaModelMeta("huge", 40 * GB, "70B", "Q4_0");
        var hw = new HardwareBudget(16 * GB, 8 * GB);
        Assert.AreEqual(OllamaFitKind.NoFit, OllamaFitPolicy.Classify(model, hw));
    }

    [TestMethod]
    public void MidModel_PartialVram_ReturnsMaybe()
    {
        // 10 GB model, 6 GB VRAM, 12 GB RAM → hybrid may work
        var model = new OllamaModelMeta("mid", 10 * GB, "13B", "Q4_K_M");
        var hw = new HardwareBudget(12 * GB, 6 * GB);
        Assert.AreEqual(OllamaFitKind.Maybe, OllamaFitPolicy.Classify(model, hw));
    }

    [TestMethod]
    public void OrderBy_FitKind_SortsCorrectly()
    {
        // Verify enum ordering: Gpu(0) < Cpu(1) < Maybe(2) < NoFit(3)
        var kinds = new[] { OllamaFitKind.NoFit, OllamaFitKind.Maybe, OllamaFitKind.Gpu, OllamaFitKind.Cpu };
        var sorted = kinds.OrderBy(k => k).ToArray();
        CollectionAssert.AreEqual(
            new[] { OllamaFitKind.Gpu, OllamaFitKind.Cpu, OllamaFitKind.Maybe, OllamaFitKind.NoFit },
            sorted);
    }

    [TestMethod]
    public void NegativeSize_ReturnsMaybe()
    {
        var model = new OllamaModelMeta("test", -1, null, null);
        var hw = new HardwareBudget(16 * GB, 8 * GB);
        Assert.AreEqual(OllamaFitKind.Maybe, OllamaFitPolicy.Classify(model, hw));
    }
}
