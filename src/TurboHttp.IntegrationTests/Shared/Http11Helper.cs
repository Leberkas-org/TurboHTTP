using System.Buffers;
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using TurboHttp.Protocol;

namespace TurboHttp.IntegrationTests.Shared;

/// <summary>
/// Manages a persistent HTTP/1.1 TCP connection for keep-alive and pipeline testing.
/// Uses <see cref="Http11Encoder"/> to send requests and <see cref="Http11Decoder"/>
/// to parse responses. Supports sequential keep-alive and response pipelining.
/// </summary>
public sealed class Http11Connection : IAsyncDisposable
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly Http11Decoder _decoder;
    private readonly byte[] _readBuffer;
    private readonly Queue<HttpResponseMessage> _pending;

    private const int ReadBufferSize = 65536;

    private Http11Connection(TcpClient tcp, NetworkStream stream, int maxHeaderSize)
    {
        _tcp = tcp;
        _stream = stream;
        _decoder = new Http11Decoder(maxHeaderSize: maxHeaderSize);
        _readBuffer = new byte[ReadBufferSize];
        _pending = new Queue<HttpResponseMessage>();
        IsServerClosed = false;
    }

    /// <summary>Opens a new TCP connection to 127.0.0.1:<paramref name="port"/>.</summary>
    public static async Task<Http11Connection> OpenAsync(int port, CancellationToken ct = default)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port, ct);
        return new Http11Connection(tcp, tcp.GetStream(), maxHeaderSize: 8192);
    }

    /// <summary>
    /// Opens a new TCP connection with a custom <paramref name="maxHeaderSize"/> for the decoder.
    /// Use when testing very large response headers that exceed the default 8 KB limit.
    /// </summary>
    public static async Task<Http11Connection> OpenWithHeaderSizeAsync(int port, int maxHeaderSize,
        CancellationToken ct = default)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port, ct);
        return new Http11Connection(tcp, tcp.GetStream(), maxHeaderSize);
    }

    /// <summary>True when the server sent a <c>Connection: close</c> response header.</summary>
    public bool IsServerClosed { get; private set; }

    /// <summary>
    /// Encodes <paramref name="request"/> and sends it on the persistent connection.
    /// Reads bytes until the decoder produces a complete response.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken externalCt = default)
    {
        var encBuf = ArrayPool<byte>.Shared.Rent(4 * 1024 * 1024);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            var ct = cts.Token;

            var isHead = request.Method == HttpMethod.Head;

            var span = encBuf.AsSpan();
            var written = Http11Encoder.Encode(request, ref span);
            await _stream.WriteAsync(encBuf.AsMemory(0, written), ct);

            return await ReadOneResponseAsync(ct, isHead);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(encBuf);
        }
    }

    /// <summary>
    /// Sends all <paramref name="requests"/> in a single batch (HTTP/1.1 pipelining)
    /// and returns all responses in order.
    /// </summary>
    public async Task<IReadOnlyList<HttpResponseMessage>> PipelineAsync(IEnumerable<HttpRequestMessage> requests,
        CancellationToken externalCt = default)
    {
        var encBuf = ArrayPool<byte>.Shared.Rent(2 * 1024 * 1024);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            var ct = cts.Token;

            var requestList = requests.ToList();
            if (requestList.Count == 0)
            {
                return [];
            }

            var totalWritten = 0;
            foreach (var request in requestList)
            {
                var span = encBuf.AsSpan(totalWritten);
                var written = Http11Encoder.Encode(request, ref span);
                totalWritten += written;
            }

            await _stream.WriteAsync(encBuf.AsMemory(0, totalWritten), ct);

            // Read responses in order (track HEAD requests for no-body decoding)
            var results = new List<HttpResponseMessage>(requestList.Count);
            foreach (var t in requestList)
            {
                var isHead = t.Method == HttpMethod.Head;
                results.Add(await ReadOneResponseAsync(ct, isHead));
            }

            return results;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(encBuf);
        }
    }

    private async Task<HttpResponseMessage> ReadOneResponseAsync(CancellationToken ct, bool isHead = false)
    {
        // Return already-decoded response if available
        if (_pending.TryDequeue(out var queued))
        {
            return queued;
        }

        while (true)
        {
            var bytesRead = await _stream.ReadAsync(_readBuffer, ct);
            if (bytesRead == 0)
            {
                throw new InvalidOperationException(
                    "Server closed connection before a complete HTTP/1.1 response was received.");
            }

            // Copy to fresh array to avoid buffer aliasing with the decoder's stored remainder
            var chunk = _readBuffer.AsMemory(0, bytesRead).ToArray();

            bool decoded;
            ImmutableList<HttpResponseMessage> responses;

            if (isHead)
            {
                // RFC 9112 §6.3: HEAD responses have no body; use no-body decoder path
                decoded = _decoder.TryDecodeHead(chunk, out responses);
            }
            else
            {
                decoded = _decoder.TryDecode(chunk, out responses);
            }

            if (decoded)
            {
                foreach (var r in responses)
                {
                    _pending.Enqueue(r);
                }
            }

            if (_pending.TryDequeue(out var response))
            {
                // Track server-initiated connection close
                if (response.Headers.Connection.Contains("close"))
                {
                    IsServerClosed = true;
                }

                return response;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _decoder.Dispose();

        try
        {
            await _stream.DisposeAsync();
        }
        catch (Exception)
        {
            // ignore errors on close
        }

        _tcp.Dispose();
        await ValueTask.CompletedTask;
    }
}

/// <summary>
/// Static helper for HTTP/1.1 integration tests.
/// Opens a new connection per call (for simple one-shot tests) or
/// returns a persistent <see cref="Http11Connection"/> for keep-alive tests.
/// </summary>
public static class Http11Helper
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Encodes <paramref name="request"/>, opens a new TCP connection, sends the request,
    /// reads one response, closes the connection, and returns the decoded response.
    /// </summary>
    public static async Task<HttpResponseMessage> SendAsync(
        int port,
        HttpRequestMessage request,
        CancellationToken externalCt = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        cts.CancelAfter(DefaultTimeout);

        await using var conn = await Http11Connection.OpenAsync(port, cts.Token);
        return await conn.SendAsync(request, cts.Token);
    }

    /// <summary>Opens a persistent HTTP/1.1 connection for keep-alive tests.</summary>
    public static Task<Http11Connection> OpenAsync(int port, CancellationToken ct = default)
        => Http11Connection.OpenAsync(port, ct);

    /// <summary>Sends GET <paramref name="path"/> over a new connection.</summary>
    public static Task<HttpResponseMessage> GetAsync(
        int port,
        string path,
        CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(port, path));
        return SendAsync(port, request, ct);
    }

    /// <summary>Sends HEAD <paramref name="path"/> over a new connection.</summary>
    public static Task<HttpResponseMessage> HeadAsync(
        int port,
        string path,
        CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Head, BuildUri(port, path));
        return SendAsync(port, request, ct);
    }

    /// <summary>Sends POST <paramref name="path"/> over a new connection.</summary>
    public static Task<HttpResponseMessage> PostAsync(
        int port,
        string path,
        HttpContent? content,
        CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(port, path))
        {
            Content = content
        };
        return SendAsync(port, request, ct);
    }

    /// <summary>Builds an absolute URI for the given path on 127.0.0.1.</summary>
    public static Uri BuildUri(int port, string path)
        => new($"http://127.0.0.1:{port}{path}");
}