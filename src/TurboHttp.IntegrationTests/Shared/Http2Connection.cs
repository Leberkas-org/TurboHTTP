using System.Net;
using System.Net.Sockets;
using TurboHttp.Protocol;

namespace TurboHttp.IntegrationTests.Shared;

/// <summary>
/// Manages a raw HTTP/2 cleartext (h2c) TCP connection for integration tests.
/// Performs the connection preface exchange, handles SETTINGS/PING ACKs automatically,
/// and sends WINDOW_UPDATE frames to replenish flow-control windows for large bodies.
/// </summary>
public sealed class Http2Connection : IAsyncDisposable
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly Http2Decoder _decoder;
    private readonly Http2Encoder _encoder;
    private readonly byte[] _readBuffer;

    // Track the connection receive window so we can send WINDOW_UPDATE when needed.
    private const int InitialReceiveWindow = 65535;
    private const int ReceiveWindowRefillThreshold = 16384; // refill when below ~25%
    private const int ReadBufferSize = 65536;
    private const int EncodeBufferSize = 2 * 1024 * 1024;

    private Http2Connection(TcpClient tcp, Http2Encoder encoder)
    {
        _tcp = tcp;
        _stream = tcp.GetStream();
        _decoder = new Http2Decoder();
        _encoder = encoder;
        _readBuffer = new byte[ReadBufferSize];
    }

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Connects to <c>127.0.0.1:<paramref name="port"/></c>, performs the HTTP/2 connection
    /// preface exchange, and returns a ready-to-use connection.
    /// </summary>
    public static async Task<Http2Connection> OpenAsync(int port, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port, cts.Token);

        var conn = new Http2Connection(tcp, new Http2Encoder());
        await conn.PerformPrefaceAsync(cts.Token);
        return conn;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Encodes <paramref name="request"/> and sends it on this connection.
    /// Returns the HTTP/2 stream ID assigned to the request.
    /// </summary>
    public async Task<int> SendRequestAsync(HttpRequestMessage request, CancellationToken ct = default)
    {
        var buffer = new byte[EncodeBufferSize];
        var memory = buffer.AsMemory();
        var (streamId, bytesWritten) = _encoder.Encode(request, ref memory);
        await _stream.WriteAsync(buffer.AsMemory(0, bytesWritten), ct);
        return streamId;
    }

    /// <summary>
    /// Sends a request and waits for the response on the same stream.
    /// Automatically processes control frames (SETTINGS ACK, PING ACK, WINDOW_UPDATE).
    /// </summary>
    public async Task<HttpResponseMessage> SendAndReceiveAsync(HttpRequestMessage request,
        CancellationToken externalCt = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var streamId = await SendRequestAsync(request, cts.Token);
        return await ReadResponseAsync(streamId, cts.Token);
    }

    /// <summary>
    /// Reads the response for the given <paramref name="streamId"/>.
    /// Processes any interleaved control frames automatically.
    /// </summary>
    public async Task<HttpResponseMessage> ReadResponseAsync(int streamId, CancellationToken ct = default)
    {
        while (true)
        {
            var bytesRead = await _stream.ReadAsync(_readBuffer, ct);
            if (bytesRead == 0)
            {
                throw new InvalidOperationException(
                    $"Server closed connection before response on stream {streamId} was received.");
            }

            var chunk = _readBuffer.AsMemory(0, bytesRead).ToArray();

            if (!_decoder.TryDecode(chunk.AsMemory(), out var result))
            {
                continue;
            }

            // Automatically send SETTINGS ACKs that the server is expecting.
            foreach (var ack in result.SettingsAcksToSend)
            {
                await _stream.WriteAsync(ack, ct);
            }

            // Automatically send PING ACKs.
            foreach (var pingAck in result.PingAcksToSend)
            {
                await _stream.WriteAsync(pingAck, ct);
            }

            // Apply any window updates from server to encoder.
            foreach (var (sid, increment) in result.WindowUpdates)
            {
                if (sid == 0)
                {
                    _encoder.UpdateConnectionWindow(increment);
                }
                else
                {
                    _encoder.UpdateStreamWindow(sid, increment);
                }
            }

            // Replenish connection and stream receive windows (for large response bodies).
            await SendConnectionWindowUpdateIfNeededAsync(ct);
            await SendStreamWindowUpdateIfNeededAsync(streamId, ct);

            // Return the response if it arrived.
            foreach (var (sid, resp) in result.Responses)
            {
                if (sid == streamId)
                {
                    return resp;
                }
            }

            // Fail fast if the server reset our stream.
            foreach (var (sid, error) in result.RstStreams)
            {
                if (sid == streamId)
                {
                    throw new Http2Exception($"Server reset stream {streamId} with error {error}.", error);
                }
            }

            // Fail fast on GOAWAY.
            if (result.GoAway is { } goAway)
            {
                throw new Http2Exception(
                    $"Server sent GOAWAY: lastStream={goAway.LastStreamId}, error={goAway.ErrorCode}.",
                    goAway.ErrorCode);
            }
        }
    }

    /// <summary>
    /// Reads frames from the connection until a full decode result arrives.
    /// Used by tests that need raw decode results (SETTINGS, PING, GOAWAY, etc.).
    /// </summary>
    public async Task<Http2DecodeResult> ReadDecodeResultAsync(CancellationToken ct = default)
    {
        while (true)
        {
            var bytesRead = await _stream.ReadAsync(_readBuffer, ct);
            if (bytesRead == 0)
            {
                throw new InvalidOperationException("Server closed connection unexpectedly.");
            }

            var chunk = _readBuffer.AsMemory(0, bytesRead).ToArray();
            if (_decoder.TryDecode(chunk.AsMemory(), out var result))
            {
                return result;
            }
        }
    }

    /// <summary>Sends raw bytes directly to the TCP stream (for low-level frame tests).</summary>
    public Task WriteRawAsync(byte[] bytes, CancellationToken ct = default) => _stream.WriteAsync(bytes, ct).AsTask();

    /// <summary>
    /// Sends a PING frame and waits for the PING ACK.
    /// Returns the 8-byte opaque data echoed in the ACK.
    /// </summary>
    public async Task<byte[]> PingAsync(byte[] data, CancellationToken ct = default)
    {
        var pingBytes = Http2Encoder.EncodePing(data);
        await _stream.WriteAsync(pingBytes, ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        while (true)
        {
            var bytesRead = await _stream.ReadAsync(_readBuffer, cts.Token);
            if (bytesRead == 0)
            {
                throw new InvalidOperationException("Server closed connection during PING exchange.");
            }

            var chunk = _readBuffer.AsMemory(0, bytesRead).ToArray();
            if (!_decoder.TryDecode(chunk.AsMemory(), out var result))
            {
                continue;
            }

            foreach (var ack in result.SettingsAcksToSend)
            {
                await _stream.WriteAsync(ack, cts.Token);
            }

            if (result.PingAcks.Count > 0)
            {
                return result.PingAcks[0];
            }
        }
    }

    /// <summary>Sends an HTTP/2 GOAWAY frame to the server.</summary>
    public Task SendGoAwayAsync(int lastStreamId, Http2ErrorCode errorCode, CancellationToken ct = default)
    {
        var frame = Http2Encoder.EncodeGoAway(lastStreamId, errorCode);
        return _stream.WriteAsync(frame, ct).AsTask();
    }

    /// <summary>Sends an HTTP/2 RST_STREAM frame for <paramref name="streamId"/>.</summary>
    public Task SendRstStreamAsync(int streamId, Http2ErrorCode errorCode, CancellationToken ct = default)
    {
        var frame = Http2Encoder.EncodeRstStream(streamId, errorCode);
        return _stream.WriteAsync(frame, ct).AsTask();
    }

    /// <summary>Sends an HTTP/2 WINDOW_UPDATE frame.</summary>
    public Task SendWindowUpdateAsync(int streamId, int increment, CancellationToken ct = default)
    {
        var frame = Http2Encoder.EncodeWindowUpdate(streamId, increment);
        return _stream.WriteAsync(frame, ct).AsTask();
    }

    /// <summary>Sends an HTTP/2 SETTINGS frame.</summary>
    public Task SendSettingsAsync(ReadOnlySpan<(SettingsParameter Key, uint Value)> parameters,
        CancellationToken ct = default)
    {
        var frame = Http2Encoder.EncodeSettings(parameters);
        return _stream.WriteAsync(frame, ct).AsTask();
    }

    /// <summary>Exposes the underlying decoder for window and state inspection by tests.</summary>
    public Http2Decoder Decoder => _decoder;

    // ── Multi-stream API ──────────────────────────────────────────────────────

    /// <summary>
    /// Sends multiple requests back-to-back without waiting for any responses.
    /// Returns the stream IDs in order of the supplied requests.
    /// </summary>
    public async Task<IReadOnlyList<int>> SendRequestsAsync(IReadOnlyList<HttpRequestMessage> requests,
        CancellationToken ct = default)
    {
        var streamIds = new List<int>(requests.Count);
        foreach (var request in requests)
        {
            streamIds.Add(await SendRequestAsync(request, ct));
        }

        return streamIds;
    }

    /// <summary>
    /// Reads responses for all <paramref name="streamIds"/>, buffering any that arrive
    /// out of order, and returns a dictionary mapping stream ID to response.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, HttpResponseMessage>> ReadAllResponsesAsync(
        IReadOnlyList<int> streamIds,
        CancellationToken externalCt = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var pending = new HashSet<int>(streamIds);
        var collected = new Dictionary<int, HttpResponseMessage>();

        while (pending.Count > 0)
        {
            var bytesRead = await _stream.ReadAsync(_readBuffer, cts.Token);
            if (bytesRead == 0)
            {
                throw new InvalidOperationException(
                    "Server closed connection before all responses were received.");
            }

            var chunk = _readBuffer.AsMemory(0, bytesRead).ToArray();
            if (!_decoder.TryDecode(chunk.AsMemory(), out var result))
            {
                continue;
            }

            foreach (var ack in result.SettingsAcksToSend)
            {
                await _stream.WriteAsync(ack, cts.Token);
            }

            foreach (var pingAck in result.PingAcksToSend)
            {
                await _stream.WriteAsync(pingAck, cts.Token);
            }

            foreach (var (sid, increment) in result.WindowUpdates)
            {
                if (sid == 0)
                {
                    _encoder.UpdateConnectionWindow(increment);
                }
                else
                {
                    _encoder.UpdateStreamWindow(sid, increment);
                }
            }

            await SendConnectionWindowUpdateIfNeededAsync(cts.Token);
            foreach (var sid in pending)
            {
                await SendStreamWindowUpdateIfNeededAsync(sid, cts.Token);
            }

            foreach (var (sid, resp) in result.Responses)
            {
                if (pending.Contains(sid))
                {
                    collected[sid] = resp;
                    pending.Remove(sid);
                }
            }

            foreach (var (sid, error) in result.RstStreams)
            {
                if (pending.Contains(sid))
                {
                    throw new Http2Exception(
                        $"Server reset stream {sid} with error {error}.",
                        error);
                }
            }

            if (result.GoAway is { } goAway && pending.Count > 0)
            {
                throw new Http2Exception(
                    $"Server sent GOAWAY: lastStream={goAway.LastStreamId}, error={goAway.ErrorCode}.",
                    goAway.ErrorCode);
            }
        }

        return collected;
    }

    /// <summary>
    /// Convenience: sends all requests then waits for all responses.
    /// Returns a dictionary mapping stream ID to response.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, HttpResponseMessage>> SendAndReceiveMultipleAsync(
        IReadOnlyList<HttpRequestMessage> requests,
        CancellationToken externalCt = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var streamIds = await SendRequestsAsync(requests, cts.Token);
        return await ReadAllResponsesAsync(streamIds, cts.Token);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private async Task PerformPrefaceAsync(CancellationToken ct)
    {
        // RFC 7540 §3.5: Send client connection preface (magic string + SETTINGS frame).
        var preface = Http2Encoder.BuildConnectionPreface();
        await _stream.WriteAsync(preface, ct);

        // Read until we receive the server's initial SETTINGS frame.
        while (true)
        {
            var bytesRead = await _stream.ReadAsync(_readBuffer, ct);
            if (bytesRead == 0)
            {
                throw new InvalidOperationException("Server closed connection during HTTP/2 handshake.");
            }

            var chunk = _readBuffer.AsMemory(0, bytesRead).ToArray();
            if (!_decoder.TryDecode(chunk.AsMemory(), out var result))
            {
                continue;
            }

            // Send SETTINGS ACK(s) required by the server.
            foreach (var ack in result.SettingsAcksToSend)
            {
                await _stream.WriteAsync(ack, ct);
            }

            // Apply server settings to encoder (e.g., MAX_FRAME_SIZE).
            foreach (var settings in result.ReceivedSettings)
            {
                _encoder.ApplyServerSettings(settings);
            }

            if (result.ReceivedSettings.Count > 0)
            {
                // Preface exchange complete.
                break;
            }
        }
    }

    private async Task SendConnectionWindowUpdateIfNeededAsync(CancellationToken ct)
    {
        var current = _decoder.GetConnectionReceiveWindow();
        if (current < ReceiveWindowRefillThreshold)
        {
            var increment = InitialReceiveWindow - current;
            if (increment > 0)
            {
                var wu = Http2Encoder.EncodeWindowUpdate(0, increment);
                await _stream.WriteAsync(wu, ct);
                // Update decoder so it knows the extra receive space is available.
                _decoder.SetConnectionReceiveWindow(InitialReceiveWindow);
            }
        }
    }

    /// <summary>
    /// Sends a stream-level WINDOW_UPDATE for <paramref name="streamId"/> when its receive window
    /// drops below the refill threshold, allowing the server to send more DATA frames on that stream.
    /// Without this, bodies larger than the initial window (65535 bytes) stall because the stream
    /// window is exhausted even though the connection window is still available.
    /// </summary>
    private async Task SendStreamWindowUpdateIfNeededAsync(int streamId, CancellationToken ct)
    {
        var current = _decoder.GetStreamReceiveWindow(streamId);
        if (current < ReceiveWindowRefillThreshold)
        {
            var increment = InitialReceiveWindow - current;
            if (increment > 0)
            {
                var wu = Http2Encoder.EncodeWindowUpdate(streamId, increment);
                await _stream.WriteAsync(wu, ct);
                // Update decoder so it accepts future DATA frames within the new window.
                _decoder.SetStreamReceiveWindow(streamId, InitialReceiveWindow);
            }
        }
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        _decoder.Reset();

        try
        {
            await _stream.DisposeAsync();
        }
        catch (Exception)
        {
            // Ignore errors on close.
        }

        _tcp.Dispose();
        await ValueTask.CompletedTask;
    }
}

/// <summary>
/// Static helper for simple one-shot HTTP/2 requests in integration tests.
/// </summary>
public static class Http2Helper
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Opens a new HTTP/2 connection, sends <paramref name="request"/>, reads one response,
    /// disposes the connection, and returns the response.
    /// </summary>
    public static async Task<HttpResponseMessage> SendAsync(int port, HttpRequestMessage request,
        CancellationToken externalCt = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        cts.CancelAfter(DefaultTimeout);

        await using var conn = await Http2Connection.OpenAsync(port, cts.Token);
        return await conn.SendAndReceiveAsync(request, cts.Token);
    }

    /// <summary>Sends GET <paramref name="path"/> over a new HTTP/2 connection.</summary>
    public static Task<HttpResponseMessage> GetAsync(int port, string path, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(port, path));
        return SendAsync(port, request, ct);
    }

    /// <summary>Sends POST <paramref name="path"/> over a new HTTP/2 connection.</summary>
    public static Task<HttpResponseMessage> PostAsync(int port, string path, HttpContent? content,
        CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(port, path))
        {
            Content = content
        };
        return SendAsync(port, request, ct);
    }

    /// <summary>Opens a persistent HTTP/2 connection.</summary>
    public static Task<Http2Connection> OpenAsync(int port, CancellationToken ct = default)
        => Http2Connection.OpenAsync(port, ct);

    /// <summary>Builds an absolute URI for the given path on 127.0.0.1.</summary>
    public static Uri BuildUri(int port, string path)
        => new($"http://127.0.0.1:{port}{path}");
}