# RSTBSharp

A C# reader and writer for resource size tables: `RSTB` from *Breath of the Wild* (Wii U and Switch)
and `RESTBL` from *Tears of the Kingdom*, with YAML conversion and size calculators for both games.

```csharp
ResourceSizeTable table = ResourceSizeTable.FromBinary(decompressed);

uint size = BotwSizeCalculator.Calculate("Actor/Pack/Foo.sbactorpack", fileBytes, wiiU: false);
table.SetSize("Actor/Pack/Foo.bactorpack", size);

string yaml = table.ToYaml(knownNames);
byte[] rebuilt = ResourceSizeTable.FromYaml(yaml).ToBinary();

uint totk = TotkSizeCalculator.Calculate("Pack/Actor/Foo.pack.zs", decompressedPack);
```

Tables are read and written decompressed (Yaz0 for `.srsizetable`, zstd for `.rsizetable.zs`).

## Build

```bash
dotnet build RSTBSharp.sln -c Release
```

## Licence

AGPL-3.0-or-later. See [license.md](license.md).
