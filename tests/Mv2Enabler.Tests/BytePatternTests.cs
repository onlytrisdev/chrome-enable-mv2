namespace Mv2Enabler.Tests;

public sealed class BytePatternTests
{
    [Fact]
    public void FindAllSupportsWildcardsAndMultipleMatches()
    {
        var pattern = BytePattern.Parse("8B 41 ?? 83");
        byte[] data = [0x00, 0x8b, 0x41, 0x68, 0x83, 0x8b, 0x41, 0x30, 0x83, 0xff];

        var matches = pattern.FindAll(data);

        Assert.Equal([1, 5], matches);
    }

    [Fact]
    public void MatchesAtRejectsOutOfRangeOffsets()
    {
        var pattern = BytePattern.Parse("AA BB");
        byte[] data = [0xaa, 0xbb];

        Assert.False(pattern.MatchesAt(data, -1));
        Assert.False(pattern.MatchesAt(data, 1));
        Assert.True(pattern.MatchesAt(data, 0));
    }
}
