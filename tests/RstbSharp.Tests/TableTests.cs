namespace RSTBSharp.Tests;

/// <summary>Round trips and edits of resource size tables in binary and YAML.</summary>
public class TableTests
{
    [Theory]
    [InlineData(RstbFormat.Rstb, false)]
    [InlineData(RstbFormat.Rstb, true)]
    [InlineData(RstbFormat.Restbl, false)]
    [InlineData(RstbFormat.Restbl, true)]
    public void BinaryRoundTrips(RstbFormat format, bool bigEndian)
    {
        ResourceSizeTable table = new(format, bigEndian);
        table.Hashes[0xFFFFFFFF] = 1;
        table.Hashes[0] = 2;
        table.Names["Actor/Pack/Collides.bactorpack"] = 3;

        byte[] data = table.ToBinary();
        ResourceSizeTable back = ResourceSizeTable.FromBinary(data);

        Assert.Equal(format, back.Format);
        Assert.Equal(bigEndian, back.BigEndian);
        Assert.Equal(table.Hashes, back.Hashes);
        Assert.Equal(table.Names, back.Names);
        Assert.Equal(data, back.ToBinary());
    }

    [Fact]
    public void RestblGrowsNameSizeForLongNames()
    {
        ResourceSizeTable table = new(RstbFormat.Restbl);
        string name = new('a', 200);
        table.Names[name] = 1;

        ResourceSizeTable back = ResourceSizeTable.FromBinary(table.ToBinary());

        Assert.Equal(202, back.NameSize);
        Assert.Equal(1u, back.Names[name]);
    }

    [Fact]
    public void RstbRejectsLongNames()
    {
        ResourceSizeTable table = new();
        table.Names[new string('a', 128)] = 1;

        Assert.Throws<InvalidOperationException>(() => table.ToBinary());
    }

    [Fact]
    public void SetSizeUsesNamesOnlyForCollisions()
    {
        ResourceSizeTable table = new();
        table.SetSize("Actor/Pack/A.bactorpack", 10);

        Assert.Single(table.Hashes);
        Assert.Empty(table.Names);
        Assert.True(table.TryGetSize("Actor/Pack/A.bactorpack", out uint size));
        Assert.Equal(10u, size);

        table.Names["Actor/Pack/B.bactorpack"] = 20;
        table.SetSize("Actor/Pack/B.bactorpack", 30);

        Assert.Equal(30u, table.Names["Actor/Pack/B.bactorpack"]);
        Assert.True(table.Remove("Actor/Pack/B.bactorpack"));
        Assert.False(table.Contains("Actor/Pack/B.bactorpack"));
    }

    [Fact]
    public void YamlRoundTrips()
    {
        ResourceSizeTable table = new(RstbFormat.Restbl) { Version = 1, NameSize = 160 };
        table.SetSize("Pack/Actor/Known.pack", 0x1000);
        table.Hashes[0x12345678] = 5;
        table.Names["Collides: with a colon"] = 7;

        string yaml = table.ToYaml(["Pack/Actor/Known.pack"]);
        ResourceSizeTable back = ResourceSizeTable.FromYaml(yaml);

        Assert.Contains("Pack/Actor/Known.pack: 4096", yaml);
        Assert.Contains("0x12345678: 5", yaml);
        Assert.Equal(table.ToBinary(), back.ToBinary());
    }

    [Fact]
    public void YamlAcceptsNamesAndHexUnderHashes()
    {
        ResourceSizeTable table = ResourceSizeTable.FromYaml("""
            format: RSTB
            endian: big
            hashes:
              Actor/Pack/Foo.bactorpack: 0x100
              0xDEADBEEF: 2
            names: {}
            """);

        Assert.True(table.BigEndian);
        Assert.True(table.TryGetSize("Actor/Pack/Foo.bactorpack", out uint size));
        Assert.Equal(0x100u, size);
        Assert.Equal(2u, table.Hashes[0xDEADBEEF]);
    }
}
