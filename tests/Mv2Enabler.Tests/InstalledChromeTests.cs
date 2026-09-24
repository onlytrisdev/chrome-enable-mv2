namespace Mv2Enabler.Tests;

public sealed class InstalledChromeTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void InstalledChromeHasSafeMv2PatchSet()
    {
        var installation = ChromeInstallationFinder.Find();
        var report = AnalysisService.Analyze(installation);

        Assert.True(report.Success, string.Join(Environment.NewLine, report.Diagnostics));
        Assert.Equal(2, report.SemanticMatchCount);
        Assert.NotNull(report.Target);
        Assert.True(
            report.Target.RuleId is "chromium.mv2-impact-checker.split-extension-copies.return-unaffected.v5"
                                 or "chromium.mv2-impact-checker.split-extension-copies.return-unaffected.v6"
                                 or "chromium.mv2-impact-checker.split-extension-copies.return-unaffected.v7",
            $"Unexpected rule ID: {report.Target.RuleId}");
        Assert.Equal(0x7f, report.Target.ExpectedByte);
        Assert.Equal(0xeb, report.Target.ReplacementByte);
        Assert.Equal(8, report.Target.AdditionalEdits.Count);
        Assert.Equal(0x7f, report.Target.AdditionalEdits[0].ExpectedByte);
        Assert.Equal(0xeb, report.Target.AdditionalEdits[0].ReplacementByte);
        Assert.Equal(0x7f, report.Target.AdditionalEdits[1].ExpectedByte);
        Assert.Equal(0xeb, report.Target.AdditionalEdits[1].ReplacementByte);
        Assert.Equal(0x0f, report.Target.AdditionalEdits[2].ExpectedByte);
        Assert.Equal(0x90, report.Target.AdditionalEdits[2].ReplacementByte);
        Assert.Equal(0x8f, report.Target.AdditionalEdits[3].ExpectedByte);
        Assert.Equal(0xe9, report.Target.AdditionalEdits[3].ReplacementByte);
        Assert.Equal(0x7f, report.Target.AdditionalEdits[4].ExpectedByte);
        Assert.Equal(0xeb, report.Target.AdditionalEdits[4].ReplacementByte);
        Assert.Equal(0x7f, report.Target.AdditionalEdits[5].ExpectedByte);
        Assert.Equal(0xeb, report.Target.AdditionalEdits[5].ReplacementByte);
        Assert.All(report.Target.AdditionalEdits.Skip(6), edit => Assert.Equal(0x00, edit.ReplacementByte));
        Assert.Equal(PatchState.Original, report.Target.State);
    }
}
