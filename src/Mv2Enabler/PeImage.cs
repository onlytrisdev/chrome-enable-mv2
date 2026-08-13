using System.Buffers.Binary;

namespace Mv2Enabler;

internal sealed record PeSection(
    string Name,
    int VirtualAddress,
    int VirtualSize,
    int RawOffset,
    int RawSize,
    uint Characteristics)
{
    public bool IsExecutable => (Characteristics & 0x20000000) != 0;
}

internal sealed class PeImage
{
    private const ushort Pe32PlusMagic = 0x20b;

    private PeImage(string path, byte[] bytes, ulong preferredImageBase, IReadOnlyList<PeSection> sections)
    {
        Path = path;
        Bytes = bytes;
        PreferredImageBase = preferredImageBase;
        Sections = sections;
    }

    public string Path { get; }
    public byte[] Bytes { get; }
    public ulong PreferredImageBase { get; }
    public IReadOnlyList<PeSection> Sections { get; }

    public static PeImage Load(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(fullPath);
        if (bytes.Length < 0x100 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
        {
            throw new InvalidDataException("File is not a valid DOS/PE image.");
        }

        var peOffset = ReadInt32(bytes, 0x3c);
        EnsureRange(bytes, peOffset, 24);
        if (bytes[peOffset] != (byte)'P' || bytes[peOffset + 1] != (byte)'E' || bytes[peOffset + 2] != 0 || bytes[peOffset + 3] != 0)
        {
            throw new InvalidDataException("PE signature is missing.");
        }

        var coff = peOffset + 4;
        var numberOfSections = ReadUInt16(bytes, coff + 2);
        var optionalHeaderSize = ReadUInt16(bytes, coff + 16);
        var optional = coff + 20;
        EnsureRange(bytes, optional, optionalHeaderSize);
        if (ReadUInt16(bytes, optional) != Pe32PlusMagic)
        {
            throw new InvalidDataException("Only 64-bit PE32+ images are supported.");
        }

        var imageBase = ReadUInt64(bytes, optional + 24);
        var sectionTable = optional + optionalHeaderSize;
        var sections = new List<PeSection>(numberOfSections);
        for (var index = 0; index < numberOfSections; index++)
        {
            var sectionOffset = checked(sectionTable + index * 40);
            EnsureRange(bytes, sectionOffset, 40);
            var nameLength = 0;
            while (nameLength < 8 && bytes[sectionOffset + nameLength] != 0)
            {
                nameLength++;
            }

            var name = System.Text.Encoding.ASCII.GetString(bytes, sectionOffset, nameLength);
            var virtualSize = ReadInt32(bytes, sectionOffset + 8);
            var virtualAddress = ReadInt32(bytes, sectionOffset + 12);
            var rawSize = ReadInt32(bytes, sectionOffset + 16);
            var rawOffset = ReadInt32(bytes, sectionOffset + 20);
            var characteristics = ReadUInt32(bytes, sectionOffset + 36);
            EnsureRange(bytes, rawOffset, rawSize);
            sections.Add(new PeSection(name, virtualAddress, virtualSize, rawOffset, rawSize, characteristics));
        }

        return new PeImage(fullPath, bytes, imageBase, sections);
    }

    public PeSection GetSection(string name) =>
        Sections.SingleOrDefault(section => string.Equals(section.Name, name, StringComparison.Ordinal))
        ?? throw new InvalidDataException($"PE section '{name}' was not found.");

    public int RawOffsetToRva(int rawOffset)
    {
        var section = Sections.SingleOrDefault(item =>
            rawOffset >= item.RawOffset && rawOffset < item.RawOffset + item.RawSize);
        if (section is null)
        {
            throw new ArgumentOutOfRangeException(nameof(rawOffset), "Raw offset is not inside a PE section.");
        }

        return checked(section.VirtualAddress + rawOffset - section.RawOffset);
    }

    private static ushort ReadUInt16(byte[] bytes, int offset)
    {
        EnsureRange(bytes, offset, sizeof(ushort));
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, sizeof(ushort)));
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
    {
        EnsureRange(bytes, offset, sizeof(uint));
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)));
    }

    private static int ReadInt32(byte[] bytes, int offset)
    {
        EnsureRange(bytes, offset, sizeof(int));
        return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)));
    }

    private static ulong ReadUInt64(byte[] bytes, int offset)
    {
        EnsureRange(bytes, offset, sizeof(ulong));
        return BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset, sizeof(ulong)));
    }

    private static void EnsureRange(byte[] bytes, int offset, int length)
    {
        if (offset < 0 || length < 0 || (long)offset + length > bytes.Length)
        {
            throw new InvalidDataException("PE structure points outside the file.");
        }
    }
}
