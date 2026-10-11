namespace RSTBSharp.Tests;

/// <summary>The BotW and TotK size rules on hand-made inputs.</summary>
public class CalculatorTests
{
    [Fact]
    public void BotwSwitchAlignsTo32()
    {
        Assert.Equal(32u + 0x1c0, BotwSizeCalculator.Calculate("System/Version.txt", new byte[5], wiiU: false));
        Assert.Equal(64u + 0x200, BotwSizeCalculator.Calculate("Actor/Pack/A.sbactorpack", new byte[33], wiiU: false));
    }

    [Fact]
    public void BotwWiiUAlignsLooseFilesTo64AndCompressedTo32()
    {
        Assert.Equal(64u + 0x140, BotwSizeCalculator.Calculate("System/Version.txt", new byte[5], wiiU: true));
        Assert.Equal(64u + 0x180, BotwSizeCalculator.Calculate("Actor/Pack/A.sbactorpack", new byte[33], wiiU: true));
        Assert.Equal(64u + 0x180, BotwSizeCalculator.Calculate("Actor/Pack/A.bactorpack", 33, wiiU: true, compressed: true));
    }

    [Fact]
    public void BotwCanonicalNamesDropTheCompressionPrefix()
    {
        Assert.Equal("Actor/Pack/A.bactorpack", BotwSizeCalculator.CanonicalName("Actor/Pack/A.sbactorpack"));
        Assert.Equal("Layout/A.sarc", BotwSizeCalculator.CanonicalName("Layout/A.sarc"));
        Assert.Equal("Layout/A.sarc", BotwSizeCalculator.CanonicalName("Layout/A.ssarc"));
        Assert.Equal("Game/A.stats", BotwSizeCalculator.CanonicalName("Game/A.stats"));
        Assert.Equal("Dir.sx/NoExtension", BotwSizeCalculator.CanonicalName("Dir.sx/NoExtension"));
    }

    [Fact]
    public void BotwReadsTheSizeFromAYaz0Header()
    {
        byte[] yaz0 = new byte[16];
        "Yaz0"u8.CopyTo(yaz0);
        yaz0[7] = 100;

        Assert.Equal(128u + 0x120, BotwSizeCalculator.Calculate("Map/A.mubin", yaz0, wiiU: true));
    }

    [Fact]
    public void BotwContentDependentTypesNeedAnEstimate()
    {
        Assert.Equal(0u, BotwSizeCalculator.Calculate("Model/A.sbfres", new byte[1000], wiiU: false, estimate: false));
        Assert.NotEqual(0u, BotwSizeCalculator.Calculate("Model/A.sbfres", new byte[1000], wiiU: false));
        Assert.Equal(0u, BotwSizeCalculator.Calculate("Actor/ModelList/A.bmodellist", new byte[1000], wiiU: false));
    }

    [Fact]
    public void TotkUsesThePathOnDisk()
    {
        Assert.Equal("Pack/Actor/A.pack", TotkSizeCalculator.CanonicalName(@"Pack\Actor\A.pack.zs"));
        Assert.Equal("Model/A.bfres", TotkSizeCalculator.CanonicalName("Model/A.bfres.mc"));
        Assert.Equal("Startup/A.ta.zs", TotkSizeCalculator.CanonicalName("Startup/A.ta.zs"));

        Assert.Equal(64u + 0x180, TotkSizeCalculator.Calculate("Pack/Actor/A.pack.zs", new byte[33]));
        Assert.Equal(64u + 0x1c0, TotkSizeCalculator.Calculate("Sound/A.casset.byml.zs", new byte[33]));
        Assert.Equal((64u + 1500) * 4, TotkSizeCalculator.Calculate("Foo/A.unknown", new byte[33]));
        Assert.Equal(0u, TotkSizeCalculator.Calculate("Pack/ZsDic.pack.zs", new byte[33]));
    }
}
