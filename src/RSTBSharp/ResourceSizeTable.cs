using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

namespace RSTBSharp;

/// <summary>The two resource size table layouts: BotW's RSTB with 128-byte names, and TotK's RESTBL with a version and name size in its header.</summary>
public enum RstbFormat
{
    Rstb,
    Restbl,
}

/// <summary>A resource size table: the heap size the game reserves for each resource, keyed by the CRC32 of its name.</summary>
public sealed class ResourceSizeTable
{
    private const int RstbNameSize = 128;
    private const int RstbHeaderSize = 12;
    private const int RestblHeaderSize = 22;

    public RstbFormat Format { get; set; }

    public bool BigEndian { get; set; }

    // Version and NameSize are RESTBL only; NameSize grows on write when a name doesn't fit.
    public uint Version { get; set; } = 1;

    public int NameSize { get; set; } = 160;

    public SortedDictionary<uint, uint> Hashes { get; } = [];

    // Resources whose CRC32 collides with another resource's, kept by full name.
    public SortedDictionary<string, uint> Names { get; } = new(StringComparer.Ordinal);

    public ResourceSizeTable(RstbFormat format = RstbFormat.Rstb, bool bigEndian = false)
    {
        Format = format;
        BigEndian = bigEndian;
    }

    public static ResourceSizeTable FromBinary(ReadOnlySpan<byte> data)
    {
        RstbFormat format = data.StartsWith("RESTBL"u8) ? RstbFormat.Restbl
            : data.StartsWith("RSTB"u8) ? RstbFormat.Rstb
            : throw new InvalidDataException("Not a resource size table: expected RSTB or RESTBL magic.");
        if (data.Length < (format is RstbFormat.Rstb ? RstbHeaderSize : RestblHeaderSize))
            throw new InvalidDataException("Resource size table header is truncated.");

        // Neither format records its byte order, so take the one whose counts add up to the file length.
        return TryRead(data, format, false) ?? TryRead(data, format, true)
            ?? throw new InvalidDataException("Resource size table counts don't match its length in either byte order.");
    }

    private static ResourceSizeTable? TryRead(ReadOnlySpan<byte> data, RstbFormat format, bool be)
    {
        ResourceSizeTable table = new(format, be);
        int pos, nameSize;
        long hashCount, nameCount;
        if (format is RstbFormat.Restbl)
        {
            table.Version = U32(data, 6, be);
            table.NameSize = nameSize = (int)U32(data, 10, be);
            (hashCount, nameCount, pos) = (U32(data, 14, be), U32(data, 18, be), RestblHeaderSize);
        }
        else
        {
            (nameSize, hashCount, nameCount, pos) = (RstbNameSize, U32(data, 4, be), U32(data, 8, be), RstbHeaderSize);
        }

        if (nameSize < 0 || pos + hashCount * 8 + nameCount * (nameSize + 4L) != data.Length) return null;

        for (long i = 0; i < hashCount; i++, pos += 8)
            table.Hashes[U32(data, pos, be)] = U32(data, pos + 4, be);

        for (long i = 0; i < nameCount; i++, pos += nameSize + 4)
        {
            ReadOnlySpan<byte> name = data.Slice(pos, nameSize);
            int end = name.IndexOf((byte)0);
            table.Names[Encoding.UTF8.GetString(end < 0 ? name : name[..end])] = U32(data, pos + nameSize, be);
        }

        return table;
    }

    public byte[] ToBinary()
    {
        bool restbl = Format is RstbFormat.Restbl;
        int nameSize = restbl ? NameSize : RstbNameSize;
        int longest = Names.Count == 0 ? 0 : Names.Keys.Max(Encoding.UTF8.GetByteCount);
        if (longest >= nameSize)
        {
            if (!restbl) throw new InvalidOperationException($"RSTB names must be under {RstbNameSize} bytes.");
            nameSize = (longest + 2) & ~1;
        }

        int pos = restbl ? RestblHeaderSize : RstbHeaderSize;
        byte[] data = new byte[pos + Hashes.Count * 8 + Names.Count * (nameSize + 4)];
        Span<byte> span = data;
        bool be = BigEndian;

        if (restbl)
        {
            "RESTBL"u8.CopyTo(span);
            WriteU32(span, 6, Version, be);
            WriteU32(span, 10, (uint)nameSize, be);
        }
        else
        {
            "RSTB"u8.CopyTo(span);
        }

        // Both headers end with the hash and name counts.
        WriteU32(span, pos - 8, (uint)Hashes.Count, be);
        WriteU32(span, pos - 4, (uint)Names.Count, be);

        foreach ((uint hash, uint size) in Hashes)
        {
            WriteU32(span, pos, hash, be);
            WriteU32(span, pos + 4, size, be);
            pos += 8;
        }

        foreach ((string name, uint size) in Names)
        {
            Encoding.UTF8.GetBytes(name, span.Slice(pos, nameSize));
            WriteU32(span, pos + nameSize, size, be);
            pos += nameSize + 4;
        }

        return data;
    }

    public static ResourceSizeTable FromYaml(string yaml) => RstbYaml.Read(yaml);

    // Hashes that match one of knownNames are written as that name.
    public string ToYaml(IEnumerable<string>? knownNames = null) => RstbYaml.Write(this, knownNames);

    public bool Contains(string name) => TryGetSize(name, out _);

    public bool TryGetSize(string name, out uint size)
        => Names.TryGetValue(name, out size) || Hashes.TryGetValue(Hash(name), out size);

    // A name goes in Names when it is already there or its hash collides with a name that is.
    public void SetSize(string name, uint size)
    {
        uint hash = Hash(name);
        if (Names.Keys.Any(n => Hash(n) == hash)) Names[name] = size;
        else Hashes[hash] = size;
    }

    public bool Remove(string name) => Names.Remove(name) || Hashes.Remove(Hash(name));

    internal static uint Hash(string name) => Crc32.HashToUInt32(Encoding.UTF8.GetBytes(name));

    private static uint U32(ReadOnlySpan<byte> data, int pos, bool be)
        => be ? BinaryPrimitives.ReadUInt32BigEndian(data[pos..]) : BinaryPrimitives.ReadUInt32LittleEndian(data[pos..]);

    private static void WriteU32(Span<byte> data, int pos, uint value, bool be)
    {
        if (be) BinaryPrimitives.WriteUInt32BigEndian(data[pos..], value);
        else BinaryPrimitives.WriteUInt32LittleEndian(data[pos..], value);
    }
}
