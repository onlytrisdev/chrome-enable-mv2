namespace Mv2Enabler;

internal sealed record SemanticProbeCandidate(int Rva, byte ManifestFieldOffset, string Kind);

internal static class SemanticCandidateProbe
{
    // This is a read-only lead finder. A candidate is never a patch target until
    // Mv2GateLocator has validated the complete rule-specific control flow.
    public static IReadOnlyList<SemanticProbeCandidate> Find(PeImage image)
    {
        var section = image.GetSection(".text");
        if (!section.IsExecutable)
        {
            return [];
        }

        var text = image.Bytes.AsSpan(section.RawOffset, section.RawSize);
        var candidates = new List<SemanticProbeCandidate>();
        var cursor = 0;
        while (cursor < text.Length - 128)
        {
            var relative = text[cursor..].IndexOf((byte)0x83);
            if (relative < 0)
            {
                break;
            }

            var position = cursor + relative;
            cursor = position + 1;
            // cmp dword ptr [register + disp8], 2
            if ((text[position + 1] & 0xF8) != 0x78 || text[position + 3] != 0x02)
            {
                continue;
            }

            var body = text.Slice(position, 128);
            if (!Contains(body, [0x83, 0xF9, 0x01]) ||
                !Contains(body, [0x83, 0xF8, 0x05]) ||
                !Contains(body, [0x83, 0xF8, 0x0A]))
            {
                continue;
            }

            var kind = Contains(body, [0x0F, 0x95, 0xC1]) &&
                       Contains(body, [0x0F, 0x95, 0xC0])
                ? "possible impact checker"
                : Contains(body, [0x0F, 0x8F])
                    ? "possible disable clone"
                    : "possible policy clone";
            candidates.Add(new SemanticProbeCandidate(
                section.VirtualAddress + position,
                text[position + 2],
                kind));
        }

        return candidates;
    }

    private static bool Contains(ReadOnlySpan<byte> data, ReadOnlySpan<byte> pattern) =>
        data.IndexOf(pattern) >= 0;
}
