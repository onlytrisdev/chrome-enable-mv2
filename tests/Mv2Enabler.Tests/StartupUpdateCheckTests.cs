namespace Mv2Enabler.Tests;

public sealed class StartupUpdateCheckTests
{
    [Fact]
    public async Task NewReleaseRequiresGuiBeforeAnyWindowlessOrAutomaticLaunch()
    {
        var update = new ReleaseUpdate(new Version(9, 0, 0), "v9.0.0", new Uri("https://github.com/"), new string('a', 64));
        var result = await StartupUpdateCheck.RunAsync(new Version(3, 9, 0),
            (version, token) => Task.FromResult<ReleaseUpdate?>(update));
        Assert.True(result.RequiresGui);
        Assert.Same(update, result.Update);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task CurrentVersionDoesNotInterruptWindowlessReuse()
    {
        var calls = 0;
        var result = await StartupUpdateCheck.RunAsync(new Version(3, 9, 0), (version, token) =>
        {
            Assert.Equal(new Version(3, 9, 0), version);
            calls++;
            return Task.FromResult<ReleaseUpdate?>(null);
        });
        Assert.False(result.RequiresGui);
        Assert.Null(result.Error);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task TimeoutIsBoundedEvenWhenProviderIgnoresCancellation()
    {
        var never = new TaskCompletionSource<ReleaseUpdate?>();
        var result = await StartupUpdateCheck.RunAsync(new Version(3, 9, 0), (version, token) => never.Task,
            TimeSpan.FromMilliseconds(30));
        Assert.False(result.RequiresGui);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("network")]
    [InlineData("metadata")]
    public async Task OfflineOrMalformedReleaseDoesNotBlockChrome(string failure)
    {
        var result = await StartupUpdateCheck.RunAsync(new Version(3, 9, 0), (version, token) =>
            throw (failure == "network" ? new HttpRequestException("HTTP 403") : new InvalidDataException("invalid digest")));
        Assert.False(result.RequiresGui);
        Assert.NotNull(result.Error);
    }
}
