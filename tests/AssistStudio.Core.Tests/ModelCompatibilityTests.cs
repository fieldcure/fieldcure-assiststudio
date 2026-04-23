using System.Runtime.Versioning;
using FieldCure.AssistStudio.Helpers;

namespace FieldCure.AssistStudio.Core.Tests;

/// <summary>
/// Tests model capability and compatibility rules used by AssistStudio core features.
/// </summary>
[TestClass]
[SupportedOSPlatform("windows")]
public class ModelCompatibilityTests
{
    private const long GB = 1024L * 1024 * 1024;

    private static HardwareSpec MakeSpec(long vramBytes) =>
        new("Test GPU", vramBytes, 32 * GB, "Windows 11");

    [TestMethod]
    public void Check_SufficientVram_Compatible()
    {
        var result = ModelCompatibility.Check(4 * GB, MakeSpec(8 * GB));
        Assert.AreEqual(CompatibilityLevel.Compatible, result);
    }

    [TestMethod]
    public void Check_TightVram_NotRecommended()
    {
        // model=6GB, VRAM=7.5GB → headroom < 2GB but >= 1GB
        var result = ModelCompatibility.Check(6 * GB, MakeSpec(7 * GB + GB / 2));
        Assert.AreEqual(CompatibilityLevel.NotRecommended, result);
    }

    [TestMethod]
    public void Check_InsufficientVram_NotCompatible()
    {
        var result = ModelCompatibility.Check(8 * GB, MakeSpec(8 * GB));
        Assert.AreEqual(CompatibilityLevel.NotCompatible, result);
    }

    [TestMethod]
    public void Check_ZeroValues_Unknown()
    {
        Assert.AreEqual(CompatibilityLevel.Unknown, ModelCompatibility.Check(0, MakeSpec(8 * GB)));
        Assert.AreEqual(CompatibilityLevel.Unknown, ModelCompatibility.Check(4 * GB, MakeSpec(0)));
    }

    [TestMethod]
    public void CheckByModelName_KnownModel_ReturnsLevel()
    {
        // llama3.1 ~4.6GB, VRAM 12GB → Compatible
        var result = ModelCompatibility.CheckByModelName("llama3.1", MakeSpec(12 * GB));
        Assert.AreEqual(CompatibilityLevel.Compatible, result);
    }

    [TestMethod]
    public void CheckByModelName_UnknownModel_ReturnsUnknown()
    {
        var result = ModelCompatibility.CheckByModelName("nonexistent-model", MakeSpec(12 * GB));
        Assert.AreEqual(CompatibilityLevel.Unknown, result);
    }
}
