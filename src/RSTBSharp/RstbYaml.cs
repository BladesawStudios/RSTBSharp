using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace RSTBSharp;

/// <summary>YAML form of a resource size table, with its hashes and names as maps of sizes. A name key under hashes stands for its CRC32.</summary>
internal static class RstbYaml
{
    public static string Write(ResourceSizeTable table, IEnumerable<string>? knownNames)
    {
        Dictionary<uint, string?> lookup = [];
        foreach (string name in knownNames ?? [])
        {
            uint hash = ResourceSizeTable.Hash(name);
            if (!table.Hashes.ContainsKey(hash)) continue;
            lookup[hash] = lookup.TryGetValue(hash, out string? other) && other != name ? null : name;
        }

        StringWriter text = new();
        Emitter e = new(text, new EmitterSettings().WithBestWidth(int.MaxValue));
        e.Emit(new StreamStart());
        e.Emit(new DocumentStart());
        e.Emit(new MappingStart());
        Plain(e, "format");
        Plain(e, table.Format is RstbFormat.Rstb ? "RSTB" : "RESTBL");
        Plain(e, "endian");
        Plain(e, table.BigEndian ? "big" : "little");
        if (table.Format is RstbFormat.Restbl)
        {
            Plain(e, "version");
            Plain(e, table.Version.ToString(CultureInfo.InvariantCulture));
            Plain(e, "name_size");
            Plain(e, table.NameSize.ToString(CultureInfo.InvariantCulture));
        }

        Plain(e, "hashes");
        e.Emit(new MappingStart());
        foreach ((uint hash, uint size) in table.Hashes)
        {
            if (lookup.GetValueOrDefault(hash) is { } name) Text(e, name);
            else Plain(e, $"0x{hash:X8}");
            Plain(e, size.ToString(CultureInfo.InvariantCulture));
        }
        e.Emit(new MappingEnd());

        Plain(e, "names");
        e.Emit(new MappingStart());
        foreach ((string name, uint size) in table.Names)
        {
            Text(e, name);
            Plain(e, size.ToString(CultureInfo.InvariantCulture));
        }
        e.Emit(new MappingEnd());

        e.Emit(new MappingEnd());
        e.Emit(new DocumentEnd(true));
        e.Emit(new StreamEnd());
        return text.ToString();
    }

    public static ResourceSizeTable Read(string yaml)
    {
        Parser p = new(new StringReader(yaml));
        p.Consume<StreamStart>();
        p.Consume<DocumentStart>();
        p.Consume<MappingStart>();

        ResourceSizeTable table = new();
        while (!p.TryConsume<MappingEnd>(out _))
        {
            Scalar key = p.Consume<Scalar>();
            switch (key.Value)
            {
                case "format":
                    table.Format = Pick(p.Consume<Scalar>(), "RSTB", RstbFormat.Rstb, "RESTBL", RstbFormat.Restbl);
                    break;
                case "endian":
                    table.BigEndian = Pick(p.Consume<Scalar>(), "big", true, "little", false);
                    break;
                case "version":
                    table.Version = UInt(p.Consume<Scalar>());
                    break;
                case "name_size":
                    table.NameSize = checked((int)UInt(p.Consume<Scalar>()));
                    break;
                case "hashes":
                    ReadSizes(p, (entry, size) => table.Hashes[HashOf(entry)] = size);
                    break;
                case "names":
                    ReadSizes(p, (entry, size) => table.Names[entry.Value] = size);
                    break;
                default:
                    throw Error(key, $"unknown key '{key.Value}'");
            }
        }

        return table;
    }

    private static void ReadSizes(Parser p, Action<Scalar, uint> add)
    {
        p.Consume<MappingStart>();
        while (!p.TryConsume<MappingEnd>(out _))
            add(p.Consume<Scalar>(), UInt(p.Consume<Scalar>()));
    }

    private static uint HashOf(Scalar key)
        => key.Style is ScalarStyle.Plain && key.Value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? UInt(key) : ResourceSizeTable.Hash(key.Value);

    private static T Pick<T>(Scalar scalar, string a, T valueA, string b, T valueB)
        => scalar.Value == a ? valueA : scalar.Value == b ? valueB : throw Error(scalar, $"expected {a} or {b}");

    private static uint UInt(Scalar scalar)
    {
        string s = scalar.Value;
        bool ok = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value)
            : uint.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        return ok ? value : throw Error(scalar, $"'{s}' is not an unsigned 32-bit integer");
    }

    private static void Plain(Emitter e, string value)
        => e.Emit(new Scalar(AnchorName.Empty, TagName.Empty, value, ScalarStyle.Plain, true, false));

    private static void Text(Emitter e, string value)
        => e.Emit(new Scalar(AnchorName.Empty, TagName.Empty, value, ScalarStyle.Any, true, true));

    private static YamlException Error(ParsingEvent at, string message) => new(at.Start, at.End, message);
}
