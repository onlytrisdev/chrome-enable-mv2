using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Mv2Enabler;

internal sealed record DevToolsTarget(string? Id, string? Type, string? Title, string? Url, string? WebSocketDebuggerUrl);

internal static class ChromeDevTools
{
    public static int WaitForPort(string profilePath, Process process, TimeSpan timeout)
    {
        var activePortFile = Path.Combine(profilePath, "DevToolsActivePort");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException($"Chrome exited early with code {process.ExitCode}.");
            }

            if (File.Exists(activePortFile))
            {
                try
                {
                    using var stream = new FileStream(activePortFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    var firstLine = reader.ReadLine();
                    if (int.TryParse(firstLine, out var port))
                    {
                        return port;
                    }
                }
                catch (IOException)
                {
                    // Chrome may still be creating the file; retry until timeout.
                }
            }

            Thread.Sleep(100);
        }

        throw new TimeoutException("Chrome did not publish DevToolsActivePort.");
    }

    public static IReadOnlyList<DevToolsTarget> GetTargets(int port)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var json = client.GetStringAsync($"http://127.0.0.1:{port}/json/list").GetAwaiter().GetResult();
        return JsonSerializer.Deserialize<List<DevToolsTarget>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];
    }

    public static void TriggerLoadUnpacked(string webSocketDebuggerUrl)
    {
        const string expression = "new Promise((resolve, reject) => {" +
                                  "let attempts = 0;" +
                                  "const open = () => {" +
                                  "const manager = document.querySelector('extensions-manager');" +
                                  "const toolbar = manager?.shadowRoot?.querySelector('extensions-toolbar')?.shadowRoot;" +
                                  "const developerMode = toolbar?.querySelector('#devMode');" +
                                  "const loadUnpacked = toolbar?.querySelector('#loadUnpacked');" +
                                  "if (developerMode && loadUnpacked) {" +
                                  "if (!developerMode.checked) developerMode.click();" +
                                  "setTimeout(() => { loadUnpacked.click(); resolve('load-unpacked-clicked'); }, 500);" +
                                  "} else if (++attempts < 50) { setTimeout(open, 100); }" +
                                  "else { reject(new Error('extensions toolbar was not ready')); }" +
                                  "}; open();" +
                                  "})";
        _ = SendCommand(webSocketDebuggerUrl, "Runtime.evaluate", new
        {
            expression,
            awaitPromise = true,
            returnByValue = true
        });
    }

    public static void Navigate(string webSocketDebuggerUrl, string url)
    {
        _ = SendCommand(webSocketDebuggerUrl, "Page.navigate", new { url });
    }

    public static JsonElement Evaluate(string webSocketDebuggerUrl, string expression)
    {
        return SendCommand(webSocketDebuggerUrl, "Runtime.evaluate", new
        {
            expression,
            awaitPromise = true,
            returnByValue = true
        });
    }

    private static JsonElement SendCommand(string webSocketDebuggerUrl, string method, object parameters)
    {
        using var socket = new ClientWebSocket();
        socket.ConnectAsync(new Uri(webSocketDebuggerUrl), CancellationToken.None).GetAwaiter().GetResult();
        var request = JsonSerializer.Serialize(new
        {
            id = 1,
            method,
            @params = parameters
        });
        var requestBytes = Encoding.UTF8.GetBytes(request);
        socket.SendAsync(requestBytes, WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult();

        var buffer = new byte[16 * 1024];
        using var response = new MemoryStream();
        while (true)
        {
            var received = socket.ReceiveAsync(buffer, CancellationToken.None).GetAwaiter().GetResult();
            response.Write(buffer, 0, received.Count);
            if (!received.EndOfMessage)
            {
                continue;
            }

            using var document = JsonDocument.Parse(response.ToArray());
            if (document.RootElement.TryGetProperty("id", out var id) && id.GetInt32() == 1)
            {
                if (document.RootElement.TryGetProperty("error", out var error))
                {
                    throw new InvalidOperationException($"DevTools Runtime.evaluate failed: {error}");
                }

                if (document.RootElement.TryGetProperty("result", out var result) &&
                    result.TryGetProperty("exceptionDetails", out var exceptionDetails))
                {
                    throw new InvalidOperationException($"DevTools command raised an exception: {exceptionDetails}");
                }

                return document.RootElement.Clone();
            }

            response.SetLength(0);
        }
    }
}
