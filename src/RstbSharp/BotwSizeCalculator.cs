using System.Buffers.Binary;

namespace RSTBSharp;

/// <summary>BotW RSTB values: exact for types whose size is the file plus a constant, BCML's estimates for bfres and AAMP.</summary>
public static class BotwSizeCalculator
{
    private readonly record struct Rule(uint Nx, uint WiiU, uint WiiUCompressed);

    private static readonly Rule Default = new(0x1c0, 0x140, 0x120);

    // Constants measured from the 1.6.0 Switch and 1.5.0 Wii U tables, added to the size aligned to 32, or to 64 for files
    // the Wii U loads uncompressed. 0 means the Wii U had no such file, so its other column applies.
    private static readonly Dictionary<string, Rule> Rules = new()
    {
        ["bactorpack"] = new(0x200, 0, 0x180),
        ["baischedule"] = new(0x440, 0x340, 0),
        ["bars"] = new(0x348, 0x1f0, 0),
        ["bassetting"] = new(0x21aa8, 0x11f0c, 0),
        ["bawareness"] = new(0xcc0, 0x800, 0),
        ["bcamanim"] = new(0x2460, 0x220c, 0),
        ["bdemo"] = new(0xca0, 0x7c0, 0),
        ["bdmgparam"] = new(0x1b30, 0xee0, 0),
        ["beco"] = new(0x1c0, 0, 0x110),
        ["belnk"] = new(0x1c0, 0, 0x110),
        ["beventpack"] = new(0x200, 0, 0x200),
        ["bfarc"] = new(0x2000, 0, 0x2000),
        ["bitemico"] = new(0x2000, 0, 0x2000),
        ["blarc"] = new(0x1000, 0, 0x2000),
        ["blod"] = new(0x540, 0x3f0, 0),
        ["bmapopen"] = new(0x2000, 0, 0x2000),
        ["bmaptex"] = new(0x2000, 0, 0x2000),
        ["bmscdef"] = new(0x8568, 0x45f8, 0),
        ["bnetfp"] = new(0x1c0, 0x130, 0),
        ["bphyscharcon"] = new(0x1c0, 0x130, 0),
        ["bphyscontact"] = new(0x1c0, 0x130, 0),
        ["bphyslayer"] = new(0x1c0, 0x130, 0),
        ["bphysmaterial"] = new(0x1c0, 0x130, 0),
        ["bphyssubmat"] = new(0x1c0, 0x130, 0),
        ["breviewtex"] = new(0x2000, 0, 0x2000),
        ["brgcon"] = new(0x1bb8, 0xe60, 0),
        ["brgconfig"] = new(0x4460, 0x2bc0, 0),
        ["bstftex"] = new(0x2000, 0, 0x2000),
        ["bumii"] = new(0x440, 0x340, 0),
        ["byaml"] = new(0x1a0, 0x110, 0),
        ["byml"] = new(0x1c8, 0x140, 0x120),
        ["esetlist"] = new(0x4000, 0, 0x4000),
        ["genvb"] = new(0x380, 0, 0x470),
        ["hknm2"] = new(0x290, 0, 0x19c),
        ["hkrb"] = new(0x1a0, 0x100, 0),
        ["hkrg"] = new(0x1a0, 0x100, 0),
        ["hktmrb"] = new(0x540, 0, 0x3e0),
        ["jpg"] = new(0x2000, 0x2000, 0),
        ["sarc"] = new(0x200, 0, 0x180),
        ["stats"] = new(0x200, 0, 0x180),
    };

    private static readonly HashSet<string> ContentDependent =
    [
        "baiprog", "baniminfo", "bas", "baslist", "batcl", "batcllist", "bbonectrl", "bchemical", "bdrop", "bfres", "bgparamlist",
        "blifecondition", "bmodellist", "bphysics", "bphyssb", "brecipe", "brgbw", "brgconfiglist", "bshop", "bxml", "hkcl", "hksc",
    ];

    // 0 means the size can't be worked out and the entry should be removed. Data may be Yaz0 compressed; only its size is read.
    public static uint Calculate(string name, ReadOnlySpan<byte> data, bool wiiU, bool estimate = true)
    {
        bool yaz0 = data.Length >= 16 && data.StartsWith("Yaz0"u8);
        return Calculate(name, yaz0 ? BinaryPrimitives.ReadUInt32BigEndian(data[4..]) : data.Length, wiiU, yaz0, estimate);
    }

    // An s-prefixed extension (.sbfres) also marks the file as compressed, which only matters on the Wii U.
    public static uint Calculate(string name, long size, bool wiiU, bool compressed, bool estimate = true)
    {
        string canonical = CanonicalName(name);
        compressed |= canonical.Length < name.Length;
        string ext = Path.GetExtension(canonical).TrimStart('.');

        long result;
        if (ContentDependent.Contains(ext))
        {
            if (!estimate) return 0;
            result = ext switch
            {
                "bfres" => GuessBfres(size, wiiU, canonical.Contains(".Tex", StringComparison.Ordinal)),
                "baniminfo" => GuessBaniminfo(size, wiiU),
                _ => GuessAamp(size, wiiU, ext),
            };
        }
        else
        {
            Rule rule = Rules.GetValueOrDefault(ext, Default);
            long constant = !wiiU ? rule.Nx
                : compressed ? (rule.WiiUCompressed != 0 ? rule.WiiUCompressed : rule.WiiU)
                : (rule.WiiU != 0 ? rule.WiiU : rule.WiiUCompressed);
            result = Align(size, wiiU && !compressed ? 64 : 32) + constant;
        }

        return (uint)Math.Clamp(result, 0, uint.MaxValue);
    }

    // The name a path goes by in the RSTB: an s-prefixed extension loses its s, except on sarc, stats and stera.
    public static string CanonicalName(string path)
    {
        path = path.Replace('\\', '/');
        ReadOnlySpan<char> ext = Path.GetExtension(path.AsSpan());
        return ext.Length > 2 && ext[1] == 's' && ext is not (".sarc" or ".stats" or ".stera")
            ? string.Concat(path.AsSpan(0, path.Length - ext.Length + 1), ext[2..])
            : path;
    }

    private static long Align(long size, long alignment) => (size + alignment - 1) / alignment * alignment;

    private static long GuessBaniminfo(long size, bool wiiU)
    {
        long value = (long)(Align(size, 32) * (size > 36864 ? 1.5 : 4) + 0xe4 + 0x24c);
        return wiiU ? value : (long)(value * 1.5);
    }

    private static readonly (long Below, double Multiplier)[] BfresTexWiiU =
    [
        (100, 9), (2_000, 7), (3_000, 5), (4_000, 4), (8_500, 3), (12_000, 2), (17_000, 1.75), (30_000, 1.5), (45_000, 1.3),
        (100_000, 1.2), (150_000, 1.1), (200_000, 1.07), (250_000, 1.045), (300_000, 1.035), (600_000, 1.03),
        (1_000_000, 1.015), (1_800_000, 1.009), (4_500_000, 1.005), (6_000_000, 1.002), (long.MaxValue, 1.0015),
    ];

    private static readonly (long Below, double Multiplier)[] BfresModelWiiU =
    [
        (500, 7), (750, 5), (1_250, 4), (2_000, 3.5), (400_000, 2.25), (600_000, 2.1), (1_000_000, 1.95),
        (1_500_000, 1.85), (3_000_000, 1.66), (long.MaxValue, 1.45),
    ];

    private static readonly (long Below, double Multiplier)[] BfresTexNx =
    [
        (10_000, 2), (30_000, 1.5), (50_000, 1.3), (long.MaxValue, 1.2),
    ];

    private static readonly (long Below, double Multiplier)[] BfresModelNx =
    [
        (1_250, 9.5), (2_500, 6), (50_000, 4.25), (100_000, 3.66), (800_000, 3.5), (2_000_000, 3.15),
        (3_000_000, 2.5), (4_000_000, 1.667), (long.MaxValue, 1.6),
    ];

    private static readonly Dictionary<string, (long Below, double Multiplier)[]> AampMultipliers = new()
    {
        ["baiprog"] = [(380, 7), (400, 6), (450, 5.5), (600, 5), (1_000, 4), (1_750, 3.5), (long.MaxValue, 3)],
        ["bas"] = [(100, 20), (200, 12.5), (300, 10), (600, 8), (1_500, 6), (2_000, 5.5), (15_000, 5), (long.MaxValue, 4.5)],
        ["baslist"] = [(100, 15), (200, 10), (300, 8), (500, 6), (800, 5), (4_000, 4), (long.MaxValue, 3.5)],
        ["bdrop"] = [(200, 8.5), (250, 7), (350, 6), (450, 5.25), (850, 4.5), (long.MaxValue, 4)],
        ["bgparamlist"] = [(100, 20), (150, 12), (250, 10), (350, 8), (450, 7), (long.MaxValue, 6)],
        ["brecipe"] = [(100, 12.5), (160, 8.5), (200, 7.5), (215, 7), (long.MaxValue, 6.5)],
        ["bshop"] = [(200, 7.25), (400, 6), (500, 5), (long.MaxValue, 4.05)],
        ["bxml"] = [(350, 6), (450, 5), (550, 4.5), (650, 4), (800, 3.5), (long.MaxValue, 3)],
    };

    private static long GuessBfres(long size, bool wiiU, bool tex)
    {
        size = (long)(size * 1.05);
        var table = wiiU ? (tex ? BfresTexWiiU : BfresModelWiiU) : (tex ? BfresTexNx : BfresModelNx);
        return (long)(size * Multiplier(table, size));
    }

    private static long GuessAamp(long size, bool wiiU, string ext)
    {
        double scaled = size * 1.05;
        if (ext == "bas") scaled *= 1.05;
        size = (long)scaled;

        double value;
        if (ext == "bphysics") value = (((size + 32) & ~31L) + 0x4e + 0x324) * Math.Max(4 * Math.Floor(size / 1388.0), 3);
        else if (AampMultipliers.TryGetValue(ext, out var table)) value = size * Multiplier(table, size);
        else return 0;

        return (long)(wiiU ? value : value * 1.5);
    }

    private static double Multiplier((long Below, double Multiplier)[] table, long size)
        => Array.Find(table, t => size < t.Below).Multiplier;
}
