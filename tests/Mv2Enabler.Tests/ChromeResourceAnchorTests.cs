namespace Mv2Enabler.Tests;

public sealed class ChromeResourceAnchorTests
{
    [Theory]
    [InlineData(8121, false)]
    [InlineData(8130, false)]
    [InlineData(65000, false)]
    [InlineData(65000, true)]
    public void FindsIdFromResourceTextNotVersionTable(int id, bool utf16) =>
        Assert.Equal(id, ChromeResourceAnchor.FindUnsupportedManifestResource(SemanticFixtures.Pak(id, utf16: utf16)));

    [Fact]
    public void RejectsAmbiguousAliasedAnchor() => Assert.Throws<InvalidDataException>(() =>
        ChromeResourceAnchor.FindUnsupportedManifestResource(SemanticFixtures.Pak(alias: true)));

    [Theory]
    [InlineData(0)]
    [InlineData(14)]
    [InlineData(20)]
    public void RejectsInvalidVersionAndBounds(int offset)
    {
        var bytes = SemanticFixtures.Pak(); SemanticFixtures.Write(bytes, offset, -1);
        Assert.Throws<InvalidDataException>(() => ChromeResourceAnchor.FindUnsupportedManifestResource(bytes));
    }

    [Fact]
    public void RejectsTruncationAndChangedWording()
    {
        var bytes = SemanticFixtures.Pak();
        Assert.Throws<InvalidDataException>(() => ChromeResourceAnchor.FindUnsupportedManifestResource(bytes[..^1]));
        bytes[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => ChromeResourceAnchor.FindUnsupportedManifestResource(bytes));
    }
}
