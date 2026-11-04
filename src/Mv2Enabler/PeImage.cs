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

    private PeImage(string path, byte[] bytes, ulong preferredImageBase, IReadOnlyList<PeSection> sections, long fileSize)
    {
        Path = path;
        Bytes = bytes;
        PreferredImageBase = preferredImageBase;
        Sections = sections;
        FileSize = fileSize;
    }

    public string Path { get; }
    public byte[] Bytes { get; }
    public ulong PreferredImageBase { get; }
    public IReadOnlyList<PeSection> Sections { get; }
    public long FileSize { get; }

    public static PeImage Load(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(fullPath);
        return Parse(fullPath, bytes, bytes.Length);
    }

    internal static PeImage LoadHeaders(Stream stream, string path)
    {
        // Warm cache reads only PE headers and planned bytes. A streaming full
        // SHA-256 still identifies the DLL: size/mtime alone are not identity.
        if (stream.Length < 0x100 || stream.Length > int.MaxValue) throw new InvalidDataException("Unsupported PE size.");
        var prefix = new byte[0x40]; stream.Position = 0; stream.ReadExactly(prefix);
        var pe = ReadInt32(prefix, 0x3c);
        if (pe < 0 || pe > 1024 * 1024 || (long)pe + 24 > stream.Length) throw new InvalidDataException("Invalid PE header offset.");
        var coff = new byte[24]; stream.Position = pe; stream.ReadExactly(coff);
        var count = ReadUInt16(coff, 6); var optional = ReadUInt16(coff, 20);
        var length = checked(pe + 24 + optional + count * 40);
        if (count > 96 || length > 1024 * 1024 || length > stream.Length) throw new InvalidDataException("Invalid PE header extent.");
        var bytes = new byte[length]; stream.Position = 0; stream.ReadExactly(bytes);
        return Parse(path, bytes, stream.Length);
    }

    internal static PeImage Parse(string path, byte[] bytes, long fileSize)
    {
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
        if (ReadUInt16(bytes, coff) != 0x8664) throw new InvalidDataException("Only x64 AMD64 images are supported.");
        var numberOfSections = ReadUInt16(bytes, coff + 2);
        var optionalHeaderSize = ReadUInt16(bytes, coff + 16);
        var optional = coff + 20;
        if (optionalHeaderSize < 112) throw new InvalidDataException("Truncated PE32+ optional header.");
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
            if (rawOffset < 0 || rawSize < 0 || virtualAddress < 0 || virtualSize < 0 ||
                (long)rawOffset + rawSize > fileSize || (long)virtualAddress + rawSize > int.MaxValue)
                throw new InvalidDataException("PE section points outside the file/address space.");
            if (sections.Any(s => rawSize > 0 && s.RawSize > 0 && rawOffset < (long)s.RawOffset + s.RawSize && s.RawOffset < (long)rawOffset + rawSize))
                throw new InvalidDataException("Overlapping PE file sections.");
            sections.Add(new PeSection(name, virtualAddress, virtualSize, rawOffset, rawSize, characteristics));
        }

        return new PeImage(System.IO.Path.GetFullPath(path), bytes, imageBase, sections, fileSize);
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
