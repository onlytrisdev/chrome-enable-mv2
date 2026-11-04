using System.Text;

namespace Mv2Enabler.Tests;

internal static class SemanticFixtures
{
    internal static PeImage Image(byte[] code, string? path = null)
    {
        var data = new byte[0x400 + code.Length];
        data[0] = (byte)'M'; data[1] = (byte)'Z'; Write(data, 0x3c, 0x80);
        data[0x80] = (byte)'P'; data[0x81] = (byte)'E';
        BitConverter.GetBytes((ushort)0x8664).CopyTo(data, 0x84);
        BitConverter.GetBytes((ushort)1).CopyTo(data, 0x86);
        BitConverter.GetBytes((ushort)0xf0).CopyTo(data, 0x94);
        BitConverter.GetBytes((ushort)0x20b).CopyTo(data, 0x98);
        BitConverter.GetBytes(0x180000000UL).CopyTo(data, 0x98 + 24);
        Encoding.ASCII.GetBytes(".text").CopyTo(data, 0x188);
        Write(data, 0x190, code.Length); Write(data, 0x194, 0x1000);
        Write(data, 0x198, code.Length); Write(data, 0x19c, 0x400); Write(data, 0x1ac, 0x60000020);
        code.CopyTo(data, 0x400);
        return PeImage.Parse(path ?? Path.Combine(Path.GetTempPath(), "mv2-synthetic.dll"), data, data.Length);
    }

    internal static byte[] Pak(int id = 65000, bool alias = false, bool utf16 = false)
    {
        var bytes = (utf16 ? Encoding.Unicode : Encoding.UTF8).GetBytes(ChromeResourceAnchor.ErrorText);
        var count = 1; var aliases = alias ? 1 : 0;
        var start = 12 + (count + 1) * 6 + aliases * 4;
        var data = new byte[start + bytes.Length]; Write(data, 0, 5); data[4] = utf16 ? (byte)2 : (byte)1;
        BitConverter.GetBytes((ushort)count).CopyTo(data, 8); BitConverter.GetBytes((ushort)aliases).CopyTo(data, 10);
        BitConverter.GetBytes((ushort)id).CopyTo(data, 12); Write(data, 14, start); Write(data, 20, data.Length);
        if (alias) BitConverter.GetBytes((ushort)(id + 1)).CopyTo(data, 24);
        bytes.CopyTo(data, start); return data;
    }
    internal static void Write(byte[] data, int offset, int value) => BitConverter.GetBytes(value).CopyTo(data, offset);
}
