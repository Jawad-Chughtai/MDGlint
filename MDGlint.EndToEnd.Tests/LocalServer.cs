using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MDGlint.EndToEnd.Tests;

/// <summary>
/// A small HTTP server on 127.0.0.1 for the tests: stands in for a remote image host or for
/// GitHub. Records every path requested.
/// </summary>
internal sealed class LocalServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Func<string, Response> _respond;
    private readonly CancellationTokenSource _stop = new();

    public LocalServer(Func<string, Response> respond)
    {
        _respond = respond;
        _listener.Start();
        _ = AcceptAsync();
    }

    public string Origin => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    public ConcurrentQueue<string> Requests { get; } = new();

    public static Response NotFound { get; } = new(404, "text/plain", "Not Found"u8.ToArray());

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var head = new StringBuilder();
                var buffer = new byte[1];
                while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    if (await stream.ReadAsync(buffer) == 0)
                    {
                        return;
                    }

                    head.Append((char)buffer[0]);
                }

                var path = head.ToString().Split(' ')[1];
                Requests.Enqueue(path);
                var response = _respond(path);
                var header = $"HTTP/1.1 {response.Status} X\r\nContent-Type: {response.ContentType}\r\n"
                    + $"Content-Length: {response.Body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
                await stream.WriteAsync(response.Body);
            }
            catch (IOException)
            {
                // The client gave up (a cancelled download, for example).
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }

    internal sealed record Response(int Status, string ContentType, byte[] Body);
}
