namespace Mv2Enabler;

internal sealed class BytePattern
{
    private readonly byte?[] _bytes;
    private readonly int _anchorIndex;
    private readonly byte _anchor;

    private BytePattern(byte?[] bytes)
    {
        if (bytes.Length == 0)
        {
            throw new ArgumentException("Pattern must not be empty.", nameof(bytes));
        }

        _bytes = bytes;
        _anchorIndex = Array.FindIndex(bytes, value => value.HasValue);
        if (_anchorIndex < 0)
        {
            throw new ArgumentException("Pattern must contain at least one fixed byte.", nameof(bytes));
        }

        _anchor = bytes[_anchorIndex]!.Value;
    }

    public int Length => _bytes.Length;

    public static BytePattern Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var bytes = new byte?[tokens.Length];

        for (var i = 0; i < tokens.Length; i++)
        {
            bytes[i] = tokens[i] is "?" or "??"
                ? null
                : Convert.ToByte(tokens[i], 16);
        }

        return new BytePattern(bytes);
    }

    public IReadOnlyList<int> FindAll(ReadOnlySpan<byte> data)
    {
        var results = new List<int>();
        var nextAnchor = _anchorIndex;

        while (nextAnchor < data.Length)
        {
            var relative = data[nextAnchor..].IndexOf(_anchor);
            if (relative < 0)
            {
                break;
            }

            var anchorPosition = nextAnchor + relative;
            var candidate = anchorPosition - _anchorIndex;
            if (candidate >= 0 && candidate + _bytes.Length <= data.Length && MatchesAt(data, candidate))
            {
                results.Add(candidate);
            }

            nextAnchor = anchorPosition + 1;
        }

        return results;
    }

    public bool MatchesAt(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset + _bytes.Length > data.Length)
        {
            return false;
        }

        for (var i = 0; i < _bytes.Length; i++)
        {
            if (_bytes[i] is { } expected && data[offset + i] != expected)
            {
                return false;
            }
        }

        return true;
    }
}
