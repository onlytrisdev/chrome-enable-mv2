namespace Mv2Enabler.Tests;

public sealed class Mv2GateLocatorTests
{
    private const int Entry = 16;
    private static readonly PeSection Text = new(".text", 0x1000, 512, 0, 512, 0x20000000);

    [Fact]
    public void ValidatesChrome155InstallPolicyFlow()
    {
        Assert.True(Mv2GateLocator.TryValidateUserMayInstallV8(
            CreateInstallFlow(), Text, Entry, out var evidence, out var reason), reason);
        Assert.Contains(evidence, item => item.Contains("8129", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(5)] // MV3 branch skips the policy call.
    [InlineData(44)] // Component-location branch goes elsewhere.
    [InlineData(36)] // Manifest-type branch has a different target.
    [InlineData(50)] // Affected-location branch no longer reaches the error.
    [InlineData(267)] // The error refers to a different localized resource.
    [InlineData(291)] // Error path no longer joins the shared callback.
    public void RejectsChangedPolicyOrErrorBranches(int offset)
    {
        var bytes = CreateInstallFlow();
        bytes[Entry + offset] ^= 1;
        Assert.False(Mv2GateLocator.TryValidateUserMayInstallV8(bytes, Text, Entry, out _, out _));
    }

    [Fact]
    public void RejectsPolicyCallOutsideExecutableSection()
    {
        var bytes = CreateInstallFlow();
        WriteRelativeTarget(bytes, Entry + 71, 5, 2048);
        Assert.False(Mv2GateLocator.TryValidateUserMayInstallV8(bytes, Text, Entry, out _, out _));
    }

    private static byte[] CreateInstallFlow()
    {
        var bytes = new byte[512];
        Copy(bytes, Entry,
            "83 7F 50 02 7F 30 48 8B 8F 28 02 00 00 8B 41 50 " +
            "80 BF 08 02 00 00 00 75 0F 8B 89 88 00 00 00 83 F9 01 0F 85 0F 01 00 00 " +
            "83 F8 05 74 09 83 F8 0A 0F 85 C9 00 00 00 " +
            "4C 8D B4 24 50 02 00 00 48 89 D9 48 89 FA 4D 89 F0 E8 00 00 00 00");
        Copy(bytes, Entry + 89, "41 80 7E 17 00");
        Copy(bytes, Entry + 255,
            "4C 8D B4 24 50 02 00 00 4C 89 F1 BA C1 1F 00 00 E8 00 00 00 00 " +
            "48 8D 4C 24 60 C6 41 F8 00 48 8D 5C 24 58 E9 32 FF FF FF");
        Copy(bytes, Entry + 311,
            "83 F9 08 0F 84 00 00 00 00 83 F9 03 0F 85 00 00 00 00 E9 00 00 00 00");
        WriteRelativeTarget(bytes, Entry + 71, 5, Entry + 400);
        WriteRelativeTarget(bytes, Entry + 255 + 16, 5, Entry + 410);
        WriteRelativeTarget(bytes, Entry + 311 + 3, 6, Entry + 40);
        WriteRelativeTarget(bytes, Entry + 311 + 12, 6, Entry + 54);
        WriteRelativeTarget(bytes, Entry + 311 + 18, 5, Entry + 40);
        return bytes;
    }

    private static void Copy(byte[] destination, int offset, string hex) =>
        Convert.FromHexString(hex.Replace(" ", string.Empty, StringComparison.Ordinal)).CopyTo(destination, offset);

    private static void WriteRelativeTarget(byte[] bytes, int offset, int length, int target) =>
        BitConverter.GetBytes(target - offset - length).CopyTo(bytes, offset + length - 4);
}
