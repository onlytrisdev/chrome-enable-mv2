using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Mv2Enabler;

internal sealed record ChromeResourceAnchor(int ResourceId, string Sha256)
{
    internal const string ErrorText = "Cannot install extension because it uses an unsupported manifest version.";

    public static ChromeResourceAnchor? Load(string dllPath)
    {
        var path = Path.Combine(Path.GetDirectoryName(dllPath)!, "Locales", "en-US.pak");
        if (!File.Exists(path)) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("English resource pack is too large.");
        var data = new byte[checked((int)stream.Length)];
        stream.ReadExactly(data);
        return new(FindUnsupportedManifestResource(data), Convert.ToHexString(SHA256.HashData(data)));
    }

    internal static int FindUnsupportedManifestResource(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12) throw new InvalidDataException("Truncated resource pack.");
        var version = BinaryPrimitives.ReadUInt32LittleEndian(data);
        int count, aliases, encoding, header;
        if (version == 5)
        {
            encoding = data[4]; count = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
            aliases = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]); header = 12;
        }
        else if (version == 4)
        {
            var n = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
            if (n > 65535) throw new InvalidDataException("Invalid resource count.");
            count = (int)n; encoding = data[8]; aliases = 0; header = 9;
        }
        else throw new InvalidDataException("Unsupported resource-pack format.");
        var tableEnd = checked(header + (count + 1) * 6 + aliases * 4);
        if (tableEnd > data.Length || encoding is not 1 and not 2)
            throw new InvalidDataException("Invalid resource table/encoding.");
        var expected = encoding == 1 ? Encoding.UTF8.GetBytes(ErrorText) : Encoding.Unicode.GetBytes(ErrorText);
        var ids = new HashSet<int>();
        var matches = new HashSet<int>();
        var indexMatches = new bool[count];
        for (var i = 0; i < count; i++)
        {
            var entry = header + i * 6;
            var id = BinaryPrimitives.ReadUInt16LittleEndian(data[entry..]);
            var start = BinaryPrimitives.ReadUInt32LittleEndian(data[(entry + 2)..]);
            var end = BinaryPrimitives.ReadUInt32LittleEndian(data[(entry + 8)..]);
            if (!ids.Add(id) || start < tableEnd || start > end || end > data.Length)
                throw new InvalidDataException("Invalid resource entry.");
            if (data.Slice((int)start, (int)(end - start)).SequenceEqual(expected))
            { matches.Add(id); indexMatches[i] = true; }
        }
        for (var i = 0; i < aliases; i++)
        {
            var entry = header + (count + 1) * 6 + i * 4;
            var id = BinaryPrimitives.ReadUInt16LittleEndian(data[entry..]);
            var index = BinaryPrimitives.ReadUInt16LittleEndian(data[(entry + 2)..]);
            if (!ids.Add(id) || index >= count) throw new InvalidDataException("Invalid resource alias.");
            if (indexMatches[index]) matches.Add(id);
        }
        if (matches.Count != 1) throw new InvalidDataException("Expected exactly one unsupported-manifest English resource.");
        return matches.Single();
    }
}
