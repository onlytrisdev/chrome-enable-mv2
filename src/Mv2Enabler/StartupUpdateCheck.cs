namespace Mv2Enabler;

internal sealed record StartupUpdateResult(ReleaseUpdate? Update, string? Error)
{
    internal bool RequiresGui => Update is not null;
}

internal static class StartupUpdateCheck
{
    internal static async Task<StartupUpdateResult> RunAsync(Version installedVersion,
        Func<Version, CancellationToken, Task<ReleaseUpdate?>>? check = null, TimeSpan? timeout = null)
    {
        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(3));
        try
        {
            var result = await (check ?? ReleaseUpdateService.CheckAsync)(installedVersion, cancellation.Token)
                .WaitAsync(cancellation.Token).ConfigureAwait(false);
            return new(result, null);
        }
        catch (OperationCanceledException) { return new(null, "Startup update check timed out."); }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or
            System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new(null, error.Message);
        }
    }
}
