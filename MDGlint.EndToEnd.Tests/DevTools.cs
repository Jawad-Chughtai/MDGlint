using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MDGlint.EndToEnd.Tests;

/// <summary>
/// A minimal DevTools protocol client for the preview page, over the port WebView2 opens when
/// started with <c>--remote-debugging-port</c>.
/// </summary>
internal sealed class DevTools : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private Task _receiving = Task.CompletedTask;
    private int _nextId;

    /// <summary>Every event the page sent, in order: its method and parameters.</summary>
    public ConcurrentQueue<(string Method, JsonElement Params)> Events { get; } = new();

    public static async Task<DevTools> ConnectAsync(int port, string pageUrlPrefix, TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow + timeout;
        var lastSeen = "the port never answered";
        while (true)
        {
            try
            {
                using var targets = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/json/list"));
                lastSeen = "targets: [" + string.Join(", ", targets.RootElement.EnumerateArray().Select(t =>
                    $"{t.GetProperty("type").GetString()} {t.GetProperty("url").GetString()}")) + "]";
                foreach (var target in targets.RootElement.EnumerateArray())
                {
                    if (target.GetProperty("type").GetString() == "page"
                        && target.GetProperty("url").GetString()!.StartsWith(pageUrlPrefix, StringComparison.Ordinal))
                    {
                        var devTools = new DevTools();
                        await devTools._socket.ConnectAsync(new Uri(target.GetProperty("webSocketDebuggerUrl").GetString()!), CancellationToken.None);
                        devTools._receiving = devTools.ReceiveAsync();
                        return devTools;
                    }
                }
            }
            catch (HttpRequestException)
            {
                // WebView2 hasn't opened the port yet.
            }
            catch (TaskCanceledException)
            {
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"No preview page appeared on DevTools port {port} ({lastSeen}).");
            }

            await Task.Delay(200);
        }
    }

    public async Task<JsonElement> SendAsync(string method, object? parameters = null)
    {
        var id = Interlocked.Increment(ref _nextId);
        var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = reply;

        var message = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters ?? new { } });
        await _sendLock.WaitAsync();
        try
        {
            await _socket.SendAsync(message, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
        }
        finally
        {
            _sendLock.Release();
        }

        var result = await reply.Task.WaitAsync(TimeSpan.FromSeconds(30));
        if (result.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException($"{method} failed: {error}");
        }

        return result.GetProperty("result");
    }

    /// <summary>Runs script in the page and returns its value (promises are awaited).</summary>
    public async Task<JsonElement> EvaluateAsync(string expression)
    {
        var result = await SendAsync("Runtime.evaluate", new { expression, returnByValue = true, awaitPromise = true });
        if (result.TryGetProperty("exceptionDetails", out var exception))
        {
            throw new InvalidOperationException($"Script failed: {exception}");
        }

        return result.GetProperty("result").TryGetProperty("value", out var value) ? value : default;
    }

    public async Task<T> EvaluateAsync<T>(string expression) =>
        (await EvaluateAsync(expression)).Deserialize<T>()!;

    /// <summary>Waits until the script returns true.</summary>
    public async Task WaitForAsync(string condition, string? because = null, double seconds = 15)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (!(await EvaluateAsync($"Boolean({condition})")).GetBoolean())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"{because ?? "Timed out waiting for: " + condition}\nThe page: {await DescribeAsync()}");
            }

            await Task.Delay(100);
        }
    }

    /// <summary>What the page shows, for failure messages.</summary>
    public async Task<string> DescribeAsync()
    {
        try
        {
            return Text(await EvaluateAsync("""
                JSON.stringify({
                  text: document.body.innerText.slice(0, 300),
                  scrollY: window.scrollY,
                  images: [...document.images].map(i => [i.getAttribute('src'), i.complete, i.naturalWidth])
                })
                """));
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            return $"(unavailable: {ex.Message})";
        }
    }

    /// <summary>
    /// Clicks the element's centre with real mouse events, so the page treats it as the user's
    /// click (links only open for those).
    /// </summary>
    public async Task ClickAsync(string selector)
    {
        var point = await EvaluateAsync<double[]>($$"""
            (() => {
              const element = document.querySelector({{JsonSerializer.Serialize(selector)}});
              element.scrollIntoView({ block: 'center' });
              const box = element.getBoundingClientRect();
              return [box.left + box.width / 2, box.top + box.height / 2];
            })()
            """);
        // As a person clicks: move there, press, release.
        await SendAsync("Input.dispatchMouseEvent", new { type = "mouseMoved", x = point[0], y = point[1] });
        foreach (var type in new[] { "mousePressed", "mouseReleased" })
        {
            await Task.Delay(50);
            await SendAsync("Input.dispatchMouseEvent", new { type, x = point[0], y = point[1], button = "left", clickCount = 1 });
        }
    }

    /// <summary>
    /// Drops files on the middle of the page as Explorer would: the page gets real File objects
    /// backed by these paths.
    /// </summary>
    public async Task DropFilesAsync(params string[] paths)
    {
        var point = await EvaluateAsync<double[]>("[window.innerWidth / 2, window.innerHeight / 2]");
        var data = new { items = Array.Empty<object>(), files = paths, dragOperationsMask = 1 };
        foreach (var type in new[] { "dragEnter", "dragOver", "drop" })
        {
            await SendAsync("Input.dispatchDragEvent", new { type, x = point[0], y = point[1], data });
            await Task.Delay(50);
        }
    }

    private async Task ReceiveAsync()
    {
        var buffer = new byte[64 * 1024];
        var message = new MemoryStream();
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var part = await _socket.ReceiveAsync(buffer, _stop.Token);
                if (part.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                message.Write(buffer, 0, part.Count);
                if (!part.EndOfMessage)
                {
                    continue;
                }

                using (var document = JsonDocument.Parse(message.ToArray()))
                {
                    var root = document.RootElement.Clone();
                    if (root.TryGetProperty("id", out var id) && _pending.TryRemove(id.GetInt32(), out var reply))
                    {
                        reply.TrySetResult(root);
                    }
                    else if (root.TryGetProperty("method", out var method))
                    {
                        Events.Enqueue((method.GetString()!, root.TryGetProperty("params", out var p) ? p : default));
                    }
                }

                message.SetLength(0);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
            // The app closed, or the test is done.
        }

        foreach (var reply in _pending.Values)
        {
            reply.TrySetException(new InvalidOperationException("The DevTools connection closed."));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try
        {
            await _receiving.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
        }

        _socket.Dispose();
        _stop.Dispose();
        _sendLock.Dispose();
    }

    internal static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.ToString();

    internal static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
}
