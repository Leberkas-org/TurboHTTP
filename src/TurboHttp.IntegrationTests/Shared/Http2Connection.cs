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
    private readonly Http2FrameDecoder _frameDecoder = new();
    private int _streamId;
    private readonly HpackDecoder _hpack = new();
    private readonly Dictionary<int, int> _streamReceiveWindows = new();
    private readonly Http2RequestEncoder _encoder;
    private readonly byte[] _readBuffer;
    private readonly Queue<byte[]> _pendingPingAcks = new();

    // Response building state
    private readonly Dictionary<int, HttpResponseMessage> _pendingResponses = new();
    private readonly Dictionary<int, List<HpackHeader>> _pendingHeaders = new();
    private readonly Dictionary<int, List<byte>> _dataBodies = new();
    private readonly Dictionary<int, HttpResponseMessage> _completedResponses = new();
    private int _continuationStreamId;
    private List<byte>? _continuationBuffer;
    private bool _continuationEndStream;
    private int _connectionReceiveWindow = InitialReceiveWindow;

    // Track the connection receive window so we can send WINDOW_UPDATE when needed.
    private const int InitialReceiveWindow = 65535;
    private const int ReceiveWindowRefillThreshold = 16384; // refill when below ~25%
    private const int ReadBufferSize = 65536;
    private const int EncodeBufferSize = 2 * 1024 * 1024;

    private Http2Connection(TcpClient tcp, Http2RequestEncoder encoder)
    {
        _tcp = tcp;
        _stream = tcp.GetStream();
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

        var conn = new Http2Connection(tcp, new Http2RequestEncoder());
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
        var (streamId, frames) = _encoder.Encode(request, _streamId);
        _streamId += 2;

        var buffer = new byte[EncodeBufferSize];
        var offset = 0;

        foreach (var frame in frames)
        {
            var frameBytes = frame.Serialize();
            if (offset + frameBytes.Length > buffer.Length)
            {
                throw new InvalidOperationException(
                    $"Frame buffer exhausted: {offset} + {frameBytes.Length} > {buffer.Length}");
            }

            frameBytes.CopyTo(buffer, offset);
            offset += frameBytes.Length;
        }

        await _stream.WriteAsync(buffer.AsMemory(0, offset), ct);
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
        // Check if the response already arrived from a previous read.
        if (_completedResponses.TryGetValue(streamId, out var cached))
        {
            _completedResponses.Remove(streamId);
            return cached;
        }

        while (true)
        {
            var bytesRead = await _stream.ReadAsync(_readBuffer, ct);
            if (bytesRead == 0)
            {
                throw new InvalidOperationException(
                    $"Server closed connection before response on stream {streamId} was received.");
            }

            var frames = _frameDecoder.Decode(_readBuffer.AsMemory(0, bytesRead));
            await ProcessFramesAsync(frames, ct);

            await SendConnectionWindowUpdateIfNeededAsync(ct);
            await SendStreamWindowUpdateIfNeededAsync(streamId, ct);

            if (_completedResponses.TryGetValue(streamId, out var response))
            {
                _completedResponses.Remove(streamId);
                return response;
            }
        }
    }

    /// <summary>
    /// Returns the connection-level receive window.
    /// Used in tests to verify flow-control accounting.
    /// </summary>
    public int GetConnectionReceiveWindow() => _connectionReceiveWindow;

    /// <summary>
    /// Returns the stream-level receive window for <paramref name="streamId"/>.
    /// Used in tests to verify flow-control accounting.
    /// </summary>
    public int GetStreamReceiveWindow(int streamId) =>
        _streamReceiveWindows.TryGetValue(streamId, out var w) ? w : InitialReceiveWindow;

    /// <summary>Sends raw bytes directly to the TCP stream (for low-level frame tests).</summary>
    public Task WriteRawAsync(byte[] bytes, CancellationToken ct = default) => _stream.WriteAsync(bytes, ct).AsTask();

    /// <summary>
    /// Sends a PING frame and waits for the PING ACK.
    /// Returns the 8-byte opaque data echoed in the ACK.
    /// </summary>
    public async Task<byte[]> PingAsync(byte[] data, CancellationToken ct = default)
    {
        var pingBytes = Http2FrameUtils.EncodePing(data);
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

            var frames = _frameDecoder.Decode(_readBuffer.AsMemory(0, bytesRead));
            await ProcessFramesAsync(frames, cts.Token);

            if (_pendingPingAcks.TryDequeue(out var ackData))
            {
                return ackData;
            }
        }
    }

    /// <summary>Sends an HTTP/2 GOAWAY frame to the server.</summary>
    public Task SendGoAwayAsync(int lastStreamId, Http2ErrorCode errorCode, CancellationToken ct = default)
    {
        var frame = Http2FrameUtils.EncodeGoAway(lastStreamId, errorCode);
        return _stream.WriteAsync(frame, ct).AsTask();
    }

    /// <summary>Sends an HTTP/2 RST_STREAM frame for <paramref name="streamId"/>.</summary>
    public Task SendRstStreamAsync(int streamId, Http2ErrorCode errorCode, CancellationToken ct = default)
    {
        var frame = Http2FrameUtils.EncodeRstStream(streamId, errorCode);
        return _stream.WriteAsync(frame, ct).AsTask();
    }

    /// <summary>Sends an HTTP/2 WINDOW_UPDATE frame.</summary>
    public Task SendWindowUpdateAsync(int streamId, int increment, CancellationToken ct = default)
    {
        var frame = Http2FrameUtils.EncodeWindowUpdate(streamId, increment);
        return _stream.WriteAsync(frame, ct).AsTask();
    }

    /// <summary>Sends an HTTP/2 SETTINGS frame.</summary>
    public Task SendSettingsAsync(ReadOnlySpan<(SettingsParameter Key, uint Value)> parameters,
        CancellationToken ct = default)
    {
        var frame = Http2FrameUtils.EncodeSettings(parameters);
        return _stream.WriteAsync(frame, ct).AsTask();
    }

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

        // Drain any responses already buffered from prior reads.
        foreach (var sid in streamIds)
        {
            if (_completedResponses.TryGetValue(sid, out var existing))
            {
                _completedResponses.Remove(sid);
                collected[sid] = existing;
                pending.Remove(sid);
            }
        }

        while (pending.Count > 0)
        {
            var bytesRead = await _stream.ReadAsync(_readBuffer, cts.Token);
            if (bytesRead == 0)
            {
                throw new InvalidOperationException(
                    "Server closed connection before all responses were received.");
            }

            var frames = _frameDecoder.Decode(_readBuffer.AsMemory(0, bytesRead));
            await ProcessFramesAsync(frames, cts.Token);

            await SendConnectionWindowUpdateIfNeededAsync(cts.Token);
            foreach (var sid in pending)
            {
                await SendStreamWindowUpdateIfNeededAsync(sid, cts.Token);
            }

            foreach (var sid in streamIds)
            {
                if (pending.Contains(sid) && _completedResponses.TryGetValue(sid, out var resp))
                {
                    _completedResponses.Remove(sid);
                    collected[sid] = resp;
                    pending.Remove(sid);
                }
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

    // ── Frame processing ──────────────────────────────────────────────────────

    private async Task ProcessFramesAsync(IReadOnlyList<Http2Frame> frames, CancellationToken ct)
    {
        foreach (var frame in frames)
        {
            switch (frame)
            {
                case HeadersFrame headers:
                    HandleHeadersFrame(headers);
                    break;
                case ContinuationFrame cont:
                    HandleContinuationFrame(cont);
                    break;
                case DataFrame data:
                    HandleDataFrame(data);
                    break;
                default:
                    await DispatchControlFrameAsync(frame, ct);
                    break;
            }
        }
    }

    private void HandleHeadersFrame(HeadersFrame frame)
    {
        var streamId = frame.StreamId;

        if (frame.EndHeaders)
        {
            var headers = _hpack.Decode(frame.HeaderBlockFragment.Span);
            _pendingHeaders[streamId] = headers;
            var response = BuildResponse(headers);
            if (response != null)
            {
                _pendingResponses[streamId] = response;
            }

            if (frame.EndStream)
            {
                FinalizeResponse(streamId);
            }
        }
        else
        {
            _continuationStreamId = streamId;
            _continuationBuffer = new List<byte>(frame.HeaderBlockFragment.ToArray());
            _continuationEndStream = frame.EndStream;
        }
    }

    private void HandleContinuationFrame(ContinuationFrame frame)
    {
        if (_continuationBuffer == null || frame.StreamId != _continuationStreamId)
        {
            return;
        }

        _continuationBuffer.AddRange(frame.HeaderBlockFragment.ToArray());

        if (frame.EndHeaders)
        {
            var block = _continuationBuffer.ToArray();
            var headers = _hpack.Decode(block);
            _pendingHeaders[_continuationStreamId] = headers;
            var response = BuildResponse(headers);
            if (response != null)
            {
                _pendingResponses[_continuationStreamId] = response;
            }

            if (_continuationEndStream)
            {
                FinalizeResponse(_continuationStreamId);
            }

            _continuationBuffer = null;
            _continuationStreamId = 0;
        }
    }

    private void HandleDataFrame(DataFrame frame)
    {
        var streamId = frame.StreamId;

        _connectionReceiveWindow -= frame.Data.Length;
        _streamReceiveWindows[streamId] = GetStreamReceiveWindow(streamId) - frame.Data.Length;

        if (!_dataBodies.TryGetValue(streamId, out var body))
        {
            body = new List<byte>();
            _dataBodies[streamId] = body;
        }

        body.AddRange(frame.Data.ToArray());

        if (frame.EndStream)
        {
            FinalizeResponse(streamId);
        }
    }

    private void FinalizeResponse(int streamId)
    {
        if (!_pendingResponses.TryGetValue(streamId, out var response))
        {
            return;
        }

        _pendingResponses.Remove(streamId);

        var bodyBytes = Array.Empty<byte>();
        if (_dataBodies.TryGetValue(streamId, out var bodyList))
        {
            _dataBodies.Remove(streamId);
            bodyBytes = bodyList.ToArray();
        }

        _pendingHeaders.TryGetValue(streamId, out var allHeaders);
        _pendingHeaders.Remove(streamId);

        var hasContentHeaders = allHeaders != null &&
                                allHeaders.Any(h => !h.Name.StartsWith(':') && IsContentHeader(h.Name));

        if (bodyBytes.Length > 0 || hasContentHeaders)
        {
            var content = new ByteArrayContent(bodyBytes);
            if (allHeaders != null)
            {
                foreach (var h in allHeaders.Where(h => !h.Name.StartsWith(':') && IsContentHeader(h.Name)))
                {
                    content.Headers.TryAddWithoutValidation(h.Name, h.Value);
                }
            }

            response.Content = content;
        }

        _completedResponses[streamId] = response;
    }

    private static HttpResponseMessage? BuildResponse(IReadOnlyList<HpackHeader> headers)
    {
        var status = headers.FirstOrDefault(h => h.Name == ":status");
        if (status == default)
        {
            return null;
        }

        var response = new HttpResponseMessage((HttpStatusCode)int.Parse(status.Value));
        foreach (var h in headers.Where(h => !h.Name.StartsWith(':') && !IsContentHeader(h.Name)))
        {
            response.Headers.TryAddWithoutValidation(h.Name, h.Value);
        }

        return response;
    }

    private static bool IsContentHeader(string name) =>
        name.ToLowerInvariant() switch
        {
            "content-type" => true,
            "content-length" => true,
            "content-encoding" => true,
            "content-language" => true,
            "content-location" => true,
            "content-md5" => true,
            "content-range" => true,
            "content-disposition" => true,
            "expires" => true,
            "last-modified" => true,
            _ => false
        };

    // ── Internal helpers ──────────────────────────────────────────────────────

    private async Task DispatchControlFrameAsync(Http2Frame frame, CancellationToken ct)
    {
        switch (frame)
        {
            case SettingsFrame { IsAck: false }:
                await _stream.WriteAsync(Http2FrameUtils.EncodeSettingsAck(), ct);
                break;
            case PingFrame { IsAck: false } ping:
                await _stream.WriteAsync(Http2FrameUtils.EncodePingAck(ping.Data), ct);
                break;
            case PingFrame { IsAck: true } ackPing:
                _pendingPingAcks.Enqueue(ackPing.Data);
                break;
            case WindowUpdateFrame wu:
                if (wu.StreamId == 0)
                {
                    _encoder.UpdateConnectionWindow(wu.Increment);
                }
                else
                {
                    _encoder.UpdateStreamWindow(wu.StreamId, wu.Increment);
                }

                break;
            case RstStreamFrame rst:
                throw new Http2Exception(
                    $"RST_STREAM on stream {rst.StreamId}: {rst.ErrorCode}",
                    rst.ErrorCode, Http2ErrorScope.Stream);
            case GoAwayFrame goAway:
                throw new Http2Exception(
                    $"GOAWAY: lastStreamId={goAway.LastStreamId} error={goAway.ErrorCode}",
                    goAway.ErrorCode, Http2ErrorScope.Connection);
        }
    }

    private async Task PerformPrefaceAsync(CancellationToken ct)
    {
        // RFC 7540 §3.5: Send client connection preface (magic string + SETTINGS frame).
        var preface = Http2FrameUtils.BuildConnectionPreface();
        await _stream.WriteAsync(preface, ct);

        // Read until we receive the server's initial SETTINGS frame.
        while (true)
        {
            var bytesRead = await _stream.ReadAsync(_readBuffer, ct);
            if (bytesRead == 0)
            {
                throw new InvalidOperationException("Server closed connection during HTTP/2 handshake.");
            }

            var frames = _frameDecoder.Decode(_readBuffer.AsMemory(0, bytesRead));
            var receivedSettings = false;

            foreach (var frame in frames)
            {
                if (frame is SettingsFrame { IsAck: false } settings)
                {
                    // Send SETTINGS ACK required by the server.
                    await _stream.WriteAsync(Http2FrameUtils.EncodeSettingsAck(), ct);
                    // Apply server settings to encoder (e.g., MAX_FRAME_SIZE).
                    _encoder.ApplyServerSettings(settings.Parameters);
                    receivedSettings = true;
                }
            }

            if (receivedSettings)
            {
                // Preface exchange complete.
                break;
            }
        }
    }

    private async Task SendConnectionWindowUpdateIfNeededAsync(CancellationToken ct)
    {
        if (_connectionReceiveWindow < ReceiveWindowRefillThreshold)
        {
            var increment = InitialReceiveWindow - _connectionReceiveWindow;
            if (increment > 0)
            {
                var wu = Http2FrameUtils.EncodeWindowUpdate(0, increment);
                await _stream.WriteAsync(wu, ct);
                _connectionReceiveWindow = InitialReceiveWindow;
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
        var current = GetStreamReceiveWindow(streamId);
        if (current < ReceiveWindowRefillThreshold)
        {
            var increment = InitialReceiveWindow - current;
            if (increment > 0)
            {
                var wu = Http2FrameUtils.EncodeWindowUpdate(streamId, increment);
                await _stream.WriteAsync(wu, ct);
                _streamReceiveWindows[streamId] = InitialReceiveWindow;
            }
        }
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        _frameDecoder.Reset();

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