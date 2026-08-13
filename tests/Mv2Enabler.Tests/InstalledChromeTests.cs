namespace Mv2Enabler.Tests;

public sealed class InstalledChromeTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void InstalledChromeHasExactlyOneSafeMv2Target()
    {
        var installation = ChromeInstallationFinder.Find();
        var report = AnalysisService.Analyze(installation);

        Assert.True(report.Success, string.Join(Environment.NewLine, report.Diagnostics));
        Assert.Equal(1, report.SemanticMatchCount);
        Assert.NotNull(report.Target);
        Assert.Equal("chromium.mv2-impact-checker.extension-overload.return-unaffected.v4", report.Target.RuleId);
        Assert.Equal(0x7f, report.Target.ExpectedByte);
        Assert.Equal(0xeb, report.Target.ReplacementByte);
        Assert.Equal(7, report.Target.AdditionalEdits.Count);
        Assert.Equal(0x7f, report.Target.AdditionalEdits[0].ExpectedByte);
        Assert.Equal(0xeb, report.Target.AdditionalEdits[0].ReplacementByte);
        Assert.All(report.Target.AdditionalEdits.Skip(5), edit => Assert.Equal(0x00, edit.ReplacementByte));
        Assert.Equal(PatchState.Original, report.Target.State);
    }
}
