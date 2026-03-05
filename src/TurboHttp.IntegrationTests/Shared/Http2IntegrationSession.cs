using System.Net;
using TurboHttp.Protocol;

namespace TurboHttp.IntegrationTests.Shared;

/// <summary>
/// Stateful HTTP/2 protocol session for integration tests.
/// Mirrors Http2ProtocolSession from TurboHttp.Tests — wraps Http2FrameDecoder with
/// stream-state tracking, flow-control accounting, and SETTINGS parsing.
/// NOT for production use.
/// </summary>
public sealed class Http2IntegrationSession
{
    private readonly Http2FrameDecoder _frameDecoder = new();
    private readonly Dictionary<int, Http2StreamLifecycleState> _streamStates = new();
    private readonly HashSet<int> _closedStreamIds = [];
    private readonly List<(int StreamId, HttpResponseMessage Response)> _responses = new();
    private readonly List<IReadOnlyList<(SettingsParameter, uint)>> _settings = new();
    private readonly List<byte[]> _pingRequests = new();
    private readonly List<(int StreamId, Http2ErrorCode Error)> _rstStreams = new();
    private readonly List<(int StreamId, int Increment)> _windowUpdates = new();
    private readonly HashSet<int> _promisedStreamIds = [];
    private readonly Dictionary<int, (HttpResponseMessage Response, List<byte> Body)> _pendingResponses = new();
    private int _connectionReceiveWindow = 65535;
    private long _connectionSendWindow = 65535;
    private readonly Dictionary<int, long> _streamSendWindows = new();
    private readonly Dictionary<int, int> _streamReceiveWindows = new();
    private int _initialWindowSize = 65535;
    private int _maxConcurrentStreams = int.MaxValue;
    private int _maxFrameSize = 16384;
    private GoAwayFrame? _goAwayFrame;
    private int _pingCount;
    private int _activeStreamCount;
    private readonly HpackDecoder _hpack = new();
    private int _continuationStreamId;
    private List<byte>? _continuationBuffer;
    private byte _continuationEndStreamFlags;
    private int _continuationCount;
    private int _rstStreamCount;
    private int _settingsCount;
    private int _emptyDataFrameCount;
    private bool _hasNewSettings;
    private readonly List<byte[]> _settingsAcksToSend = new();

    public bool IsGoingAway => _goAwayFrame is not null;
    public GoAwayFrame? GoAwayFrame => _goAwayFrame;
    public int GoAwayLastStreamId => _goAwayFrame?.LastStreamId ?? int.MaxValue;
    public int ActiveStreamCount => _activeStreamCount;
    public int MaxConcurrentStreams => _maxConcurrentStreams;
    public int ConnectionReceiveWindow => _connectionReceiveWindow;
    public long ConnectionSendWindow => _connectionSendWindow;
    public int PingCount => _pingCount;
    public int ClosedStreamCount => _closedStreamIds.Count;

    public IReadOnlyList<(int StreamId, HttpResponseMessage Response)> Responses => _responses;
    public IReadOnlyList<IReadOnlyList<(SettingsParameter, uint)>> ReceivedSettings => _settings;
    public IReadOnlyList<byte[]> PingRequests => _pingRequests;
    public IReadOnlyList<(int StreamId, Http2ErrorCode Error)> RstStreams => _rstStreams;
    public IReadOnlyList<(int StreamId, int Increment)> WindowUpdates => _windowUpdates;
    public IReadOnlyCollection<int> PromisedStreamIds => _promisedStreamIds;
    public bool HasNewSettings => _hasNewSettings;
    public IReadOnlyList<byte[]> SettingsAcksToSend => _settingsAcksToSend;

    public Http2StreamLifecycleState GetStreamState(int streamId) =>
        _streamStates.GetValueOrDefault(streamId, Http2StreamLifecycleState.Idle);

    public int GetStreamReceiveWindow(int streamId) =>
        _streamReceiveWindows.GetValueOrDefault(streamId, _initialWindowSize);

    public long GetStreamSendWindow(int streamId) =>
        _streamSendWindows.GetValueOrDefault(streamId, _initialWindowSize);

    public void SetConnectionReceiveWindow(int value) => _connectionReceiveWindow = value;

    public void SetStreamReceiveWindow(int streamId, int value) =>
        _streamReceiveWindows[streamId] = value;

    public IReadOnlyList<Http2Frame> Process(ReadOnlyMemory<byte> data)
    {
        _hasNewSettings = false;
        _settingsAcksToSend.Clear();
        var decoded = _frameDecoder.Decode(data);
        var visible = new List<Http2Frame>(decoded.Count);
        foreach (var frame in decoded)
        {
            if (frame is UnknownFrame)
            {
                visible.Add(frame);
                continue;
            }

            Dispatch(frame);
            visible.Add(frame);
        }

        return visible;
    }

    public void Reset()
    {
        _streamStates.Clear();
        _closedStreamIds.Clear();
        _responses.Clear();
        _settings.Clear();
        _pingRequests.Clear();
        _rstStreams.Clear();
        _windowUpdates.Clear();
        _promisedStreamIds.Clear();
        _pendingResponses.Clear();
        _connectionReceiveWindow = 65535;
        _connectionSendWindow = 65535;
        _streamSendWindows.Clear();
        _streamReceiveWindows.Clear();
        _initialWindowSize = 65535;
        _maxConcurrentStreams = int.MaxValue;
        _maxFrameSize = 16384;
        _goAwayFrame = null;
        _pingCount = 0;
        _activeStreamCount = 0;
        _continuationStreamId = 0;
        _continuationBuffer = null;
        _continuationEndStreamFlags = 0;
        _continuationCount = 0;
        _rstStreamCount = 0;
        _settingsCount = 0;
        _emptyDataFrameCount = 0;
        _hasNewSettings = false;
        _settingsAcksToSend.Clear();
        _frameDecoder.Reset();
    }

    private void Dispatch(Http2Frame frame)
    {
        // RFC 7540 §6.10: After HEADERS without END_HEADERS, only CONTINUATION is allowed.
        if (_continuationBuffer != null && frame is not ContinuationFrame)
        {
            throw new Http2Exception($"Expected CONTINUATION frame but received {frame.GetType().Name}");
        }

        switch (frame)
        {
            case HeadersFrame h: HandleHeaders(h); break;
            case DataFrame d: HandleData(d); break;
            case SettingsFrame s: HandleSettings(s); break;
            case PingFrame p: HandlePing(p); break;
            case WindowUpdateFrame w: HandleWindowUpdate(w); break;
            case RstStreamFrame r: HandleRst(r); break;
            case GoAwayFrame g: HandleGoAway(g); break;
            case ContinuationFrame c: HandleContinuation(c); break;
            case PushPromiseFrame pp: HandlePushPromise(pp); break;
        }
    }

    private void HandleHeaders(HeadersFrame frame)
    {
        var streamId = frame.StreamId;

        if (streamId == 0)
        {
            throw new Http2Exception("HEADERS on stream 0 is a connection error");
        }

        // RFC 7540 §6.8: After GOAWAY, reject new streams with IDs > lastStreamId.
        if (_goAwayFrame is not null && streamId > _goAwayFrame.LastStreamId)
        {
            throw new Http2Exception(
                $"HEADERS on stream {streamId} after GOAWAY with lastStreamId={_goAwayFrame.LastStreamId}");
        }

        // RFC 7540 §5.1.1: Server-initiated (even) stream IDs must be pre-announced via PUSH_PROMISE.
        if (streamId % 2 == 0 && !_promisedStreamIds.Contains(streamId))
        {
            throw new Http2Exception($"HEADERS on even stream {streamId} without prior PUSH_PROMISE");
        }

        var currentState = GetStreamState(streamId);

        if (currentState == Http2StreamLifecycleState.Idle)
        {
            if (_maxConcurrentStreams != int.MaxValue && _activeStreamCount >= _maxConcurrentStreams)
            {
                throw new Http2Exception(
                    $"MAX_CONCURRENT_STREAMS ({_maxConcurrentStreams}) exceeded: stream {streamId} refused (RFC 7540 §6.5.2)",
                    Http2ErrorCode.RefusedStream, Http2ErrorScope.Stream, streamId);
            }

            _activeStreamCount++;
            _streamSendWindows[streamId] = _initialWindowSize;
            _streamReceiveWindows[streamId] = _initialWindowSize;
        }
        else if (currentState == Http2StreamLifecycleState.Closed)
        {
            // RFC 7540 §6.2 / §5.1: HEADERS on a closed stream is a connection error of type STREAM_CLOSED.
            throw new Http2Exception($"HEADERS on closed stream {streamId}", Http2ErrorCode.StreamClosed);
        }

        if (frame.EndHeaders)
        {
            IReadOnlyList<HpackHeader> headers;
            try
            {
                headers = _hpack.Decode(frame.HeaderBlockFragment.Span);
            }
            catch (HpackException ex)
            {
                throw new Http2Exception(
                    $"RFC 9113 §4.3: HPACK decompression failure — {ex.Message}",
                    Http2ErrorCode.CompressionError, Http2ErrorScope.Connection);
            }

            ValidateHeaders(headers);
            var response = BuildResponse(headers, streamId);

            if (frame.EndStream)
            {
                // Complete response — no body incoming.
                if (response != null)
                {
                    _responses.Add((streamId, response));
                }

                MarkClosed(streamId);
            }
            else
            {
                // Headers received; body expected via DATA frames.
                if (response != null)
                {
                    _pendingResponses[streamId] = (response, []);
                }

                _streamStates[streamId] = Http2StreamLifecycleState.Open;
            }
        }
        else
        {
            _continuationStreamId = streamId;
            _continuationBuffer = new List<byte>(frame.HeaderBlockFragment.ToArray());
            _continuationEndStreamFlags = frame.EndStream ? (byte)1 : (byte)0;
            _streamStates[streamId] = Http2StreamLifecycleState.Open;
        }
    }

    private void HandleContinuation(ContinuationFrame frame)
    {
        if (_continuationBuffer == null || frame.StreamId != _continuationStreamId)
        {
            var actual = frame.StreamId;
            var expected = _continuationStreamId;
            throw new Http2Exception(
                _continuationBuffer == null
                    ? $"Unexpected CONTINUATION frame on stream {actual}; no pending header block"
                    : $"Unexpected CONTINUATION frame on stream {actual}; expected stream {expected}");
        }

        _continuationCount++;
        if (_continuationCount >= 1000)
        {
            throw new Http2Exception(
                "RFC 7540 security: Excessive CONTINUATION frames — possible CONTINUATION flood attack.",
                Http2ErrorCode.ProtocolError);
        }

        _continuationBuffer.AddRange(frame.HeaderBlockFragment.ToArray());

        if (frame.EndHeaders)
        {
            var block = _continuationBuffer.ToArray().AsMemory();
            IReadOnlyList<HpackHeader> headers;
            try
            {
                headers = _hpack.Decode(block.Span);
            }
            catch (HpackException ex)
            {
                throw new Http2Exception(
                    $"RFC 9113 §4.3: HPACK decompression failure — {ex.Message}",
                    Http2ErrorCode.CompressionError, Http2ErrorScope.Connection);
            }

            ValidateHeaders(headers);
            var response = BuildResponse(headers, _continuationStreamId);

            var endStream = _continuationEndStreamFlags != 0;
            if (endStream)
            {
                if (response != null)
                {
                    _responses.Add((_continuationStreamId, response));
                }

                MarkClosed(_continuationStreamId);
            }
            else
            {
                if (response != null)
                {
                    _pendingResponses[_continuationStreamId] = (response, []);
                }

                _streamStates[_continuationStreamId] = Http2StreamLifecycleState.Open;
            }

            _continuationCount = 0;
            _continuationBuffer = null;
            _continuationStreamId = 0;
        }
    }

    private void HandleData(DataFrame frame)
    {
        var streamId = frame.StreamId;

        // RFC 7540 §4.2: Frame size exceeded is a connection error of type FRAME_SIZE_ERROR.
        // Check before stream state so the correct error code is returned.
        if (frame.Data.Length > _maxFrameSize)
        {
            throw new Http2Exception(
                $"RFC 7540 §4.2: DATA frame payload {frame.Data.Length} exceeds MAX_FRAME_SIZE {_maxFrameSize}.",
                Http2ErrorCode.FrameSizeError);
        }

        if (streamId == 0)
        {
            throw new Http2Exception("DATA on stream 0 is a connection error");
        }

        var state = GetStreamState(streamId);

        if (state == Http2StreamLifecycleState.Idle)
        {
            // RFC 9113 §5.1: DATA on an idle stream is a connection PROTOCOL_ERROR.
            throw new Http2Exception($"DATA on idle stream {streamId}", Http2ErrorCode.ProtocolError);
        }

        if (state == Http2StreamLifecycleState.Closed)
        {
            throw new Http2Exception($"DATA on closed stream {streamId}",
                Http2ErrorCode.StreamClosed, Http2ErrorScope.Stream, streamId);
        }

        // Security: empty DATA frame exhaustion protection.
        if (frame.Data.Length == 0)
        {
            _emptyDataFrameCount++;
            if (_emptyDataFrameCount > 10000)
            {
                throw new Http2Exception(
                    "RFC 7540 security: Excessive empty DATA frames — possible resource exhaustion attack.",
                    Http2ErrorCode.ProtocolError);
            }
        }

        // RFC 7540 §6.9.1: Check flow-control windows before consuming data.
        if (frame.Data.Length > 0)
        {
            if (frame.Data.Length > _connectionReceiveWindow)
            {
                throw new Http2Exception(
                    $"DATA of {frame.Data.Length} bytes exceeds connection receive window of {_connectionReceiveWindow}",
                    Http2ErrorCode.FlowControlError);
            }

            if (_streamReceiveWindows.TryGetValue(streamId, out var streamWin) && frame.Data.Length > streamWin)
            {
                throw new Http2Exception(
                    $"DATA of {frame.Data.Length} bytes exceeds stream {streamId} receive window of {streamWin}",
                    Http2ErrorCode.FlowControlError, Http2ErrorScope.Stream, streamId);
            }
        }

        // Accumulate body bytes for the pending response.
        if (_pendingResponses.TryGetValue(streamId, out var pending))
        {
            pending.Body.AddRange(frame.Data.ToArray());
        }

        _connectionReceiveWindow -= frame.Data.Length;
        if (_streamReceiveWindows.TryGetValue(streamId, out var sw))
        {
            _streamReceiveWindows[streamId] = sw - frame.Data.Length;
        }

        if (frame.EndStream && state == Http2StreamLifecycleState.Open)
        {
            // Body complete — promote pending response to the responses list.
            if (_pendingResponses.TryGetValue(streamId, out var completePending))
            {
                var previousContent = completePending.Response.Content;
                var rawBody = completePending.Body.ToArray();

                // RFC 9110 §8.4: Apply content-encoding decompression.
                var contentEncoding = previousContent.Headers.ContentEncoding
                    .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e) &&
                        !e.Equals("identity", StringComparison.OrdinalIgnoreCase));
                var decompressed = contentEncoding != null;
                var bodyBytes = decompressed
                    ? ContentEncodingDecoder.Decompress(rawBody, contentEncoding)
                    : rawBody;

                var bodyContent = new ByteArrayContent(bodyBytes);
                // Preserve content headers; strip Content-Encoding and update Content-Length after decompression.
                foreach (var h in previousContent.Headers)
                {
                    if (h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (decompressed && h.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    bodyContent.Headers.TryAddWithoutValidation(h.Key, h.Value);
                }

                if (decompressed)
                {
                    bodyContent.Headers.ContentLength = bodyBytes.Length;
                }

                completePending.Response.Content = bodyContent;
                _responses.Add((streamId, completePending.Response));
                _pendingResponses.Remove(streamId);
            }

            MarkClosed(streamId);
        }
    }

    private void HandleSettings(SettingsFrame frame)
    {
        if (frame.IsAck)
        {
            return;
        }

        // Security: SETTINGS flood protection.
        _settingsCount++;
        if (_settingsCount > 100)
        {
            throw new Http2Exception(
                $"RFC 7540 security: Excessive SETTINGS frames ({_settingsCount}) — possible SETTINGS flood attack.",
                Http2ErrorCode.EnhanceYourCalm);
        }

        var parameters = frame.Parameters;
        _settings.Add(parameters);
        _hasNewSettings = true;
        _settingsAcksToSend.Add(SettingsFrame.SettingsAck());
        ApplySettingsParameters(parameters);
    }

    private void ApplySettingsParameters(IReadOnlyList<(SettingsParameter, uint)> parameters)
    {
        foreach (var (param, value) in parameters)
        {
            switch (param)
            {
                case SettingsParameter.MaxFrameSize:
                    if (value < 16384 || value > 16777215)
                    {
                        throw new Http2Exception(
                            $"RFC 7540 §6.5.2: SETTINGS_MAX_FRAME_SIZE {value} is outside the valid range [16384, 16777215].");
                    }

                    _maxFrameSize = (int)value;
                    break;

                case SettingsParameter.EnablePush:
                    if (value > 1)
                    {
                        throw new Http2Exception(
                            $"RFC 7540 §6.5.2: SETTINGS_ENABLE_PUSH value {value} is invalid; only 0 or 1 are permitted.",
                            Http2ErrorCode.ProtocolError);
                    }

                    break;

                case SettingsParameter.InitialWindowSize:
                    if (value > 0x7FFFFFFFu)
                    {
                        throw new Http2Exception(
                            $"RFC 7540 §6.5.2: SETTINGS_INITIAL_WINDOW_SIZE value {value} exceeds maximum 2^31-1.",
                            Http2ErrorCode.FlowControlError);
                    }

                {
                    var newInitial = (int)value;
                    var delta = (long)newInitial - _initialWindowSize;
                    foreach (var sid in _streamSendWindows.Keys.ToList())
                    {
                        var state = GetStreamState(sid);
                        if (state == Http2StreamLifecycleState.Closed || state == Http2StreamLifecycleState.Idle)
                        {
                            continue;
                        }

                        var current = _streamSendWindows[sid];
                        var updated = current + delta;
                        if (updated > 0x7FFFFFFF)
                        {
                            throw new Http2Exception(
                                $"RFC 7540 §6.9.2: SETTINGS_INITIAL_WINDOW_SIZE update would overflow stream {sid} send window.",
                                Http2ErrorCode.FlowControlError);
                        }

                        _streamSendWindows[sid] = updated;
                    }

                    _initialWindowSize = newInitial;
                }
                    break;

                case SettingsParameter.MaxConcurrentStreams:
                    _maxConcurrentStreams = (int)value;
                    break;
            }
        }
    }

    private void HandlePing(PingFrame frame)
    {
        if (!frame.IsAck)
        {
            _pingCount++;
            if (_pingCount > 1000)
            {
                throw new Http2Exception(
                    "RFC 7540 security: Excessive PING frames — possible PING flood attack.",
                    Http2ErrorCode.EnhanceYourCalm);
            }

            _pingRequests.Add(frame.Data);
        }
    }

    private void HandleWindowUpdate(WindowUpdateFrame frame)
    {
        var streamId = frame.StreamId;
        if (streamId == 0)
        {
            var newWindow = _connectionSendWindow + frame.Increment;
            if (newWindow > 0x7FFFFFFF)
            {
                throw new Http2Exception(
                    "WINDOW_UPDATE would overflow connection send window",
                    Http2ErrorCode.FlowControlError);
            }

            _connectionSendWindow = newWindow;
        }
        else
        {
            var current = _streamSendWindows.GetValueOrDefault(streamId, _initialWindowSize);
            var newWindow = current + frame.Increment;
            if (newWindow > 0x7FFFFFFF)
            {
                throw new Http2Exception(
                    $"WINDOW_UPDATE would overflow stream {streamId} send window",
                    Http2ErrorCode.FlowControlError, Http2ErrorScope.Stream, streamId);
            }

            _windowUpdates.Add((streamId, frame.Increment));
            _streamSendWindows[streamId] = newWindow;
        }
    }

    private void HandleRst(RstStreamFrame frame)
    {
        _rstStreamCount++;
        if (_rstStreamCount > 100)
        {
            throw new Http2Exception(
                "RFC 7540 security: Rapid RST_STREAM cycling — possible CVE-2023-44487 (Rapid Reset) attack.",
                Http2ErrorCode.ProtocolError);
        }

        _rstStreams.Add((frame.StreamId, frame.ErrorCode));
        MarkClosed(frame.StreamId);
    }

    private void HandleGoAway(GoAwayFrame frame)
    {
        _goAwayFrame = frame;

        // RFC 7540 §6.8: Streams with ID > lastStreamId were not processed; mark them Closed.
        foreach (var streamId in _streamStates.Keys.ToList())
        {
            if (streamId > frame.LastStreamId && _streamStates[streamId] == Http2StreamLifecycleState.Open)
            {
                MarkClosed(streamId);
            }
        }
    }

    private void HandlePushPromise(PushPromiseFrame frame)
    {
        _promisedStreamIds.Add(frame.PromisedStreamId);
    }

    private void MarkClosed(int streamId)
    {
        _streamStates[streamId] = Http2StreamLifecycleState.Closed;
        _closedStreamIds.Add(streamId);
        _activeStreamCount = Math.Max(0, _activeStreamCount - 1);
    }

    private static void ValidateHeaders(IReadOnlyList<HpackHeader> headers)
    {
        if (headers.Count == 0)
        {
            throw new Http2Exception(
                "RFC 9113 §8.3.2: Response HEADERS block is missing the required :status pseudo-header.",
                Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
        }

        var seenRegular = false;
        var seenStatus = false;

        foreach (var h in headers)
        {
            if (h.Name.StartsWith(':'))
            {
                if (seenRegular)
                {
                    throw new Http2Exception(
                        $"RFC 9113 §8.3: Pseudo-header '{h.Name}' must not appear after regular header.",
                        Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
                }

                foreach (var c in h.Name)
                {
                    if (char.IsUpper(c))
                    {
                        throw new Http2Exception(
                            $"RFC 9113 §8.2: Pseudo-header name '{h.Name}' contains uppercase characters.",
                            Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
                    }
                }

                if (h.Name == ":status")
                {
                    if (seenStatus)
                    {
                        throw new Http2Exception(
                            "RFC 9113 §8.3.2: Duplicate :status pseudo-header.",
                            Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
                    }

                    seenStatus = true;
                }
                else if (IsRequestPseudoHeader(h.Name))
                {
                    throw new Http2Exception(
                        $"RFC 9113 §8.3.2: Request pseudo-header '{h.Name}' is not valid in a response.",
                        Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
                }
                else
                {
                    throw new Http2Exception(
                        $"RFC 9113 §8.3: Unknown pseudo-header '{h.Name}' in response.",
                        Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
                }
            }
            else
            {
                seenRegular = true;

                foreach (var c in h.Name)
                {
                    if (char.IsUpper(c))
                    {
                        throw new Http2Exception(
                            $"RFC 9113 §8.2: Header field name '{h.Name}' contains uppercase characters; all names must be lowercase.",
                            Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
                    }
                }

                if (IsForbiddenConnectionHeader(h.Name))
                {
                    throw new Http2Exception(
                        $"RFC 9113 §8.2.2: Header '{h.Name}' is forbidden in HTTP/2.",
                        Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
                }
            }
        }

        if (!seenStatus)
        {
            throw new Http2Exception(
                "RFC 9113 §8.3.2: Response HEADERS block is missing the required :status pseudo-header.",
                Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
        }
    }

    private static bool IsRequestPseudoHeader(string name) =>
        name is ":method" or ":path" or ":scheme" or ":authority";

    private static bool IsForbiddenConnectionHeader(string name) =>
        name is "connection" or "keep-alive" or "proxy-connection" or "transfer-encoding" or "upgrade";

    private static HttpResponseMessage? BuildResponse(
        IReadOnlyList<HpackHeader> headers, int streamId)
    {
        var status = headers.FirstOrDefault(h => h.Name == ":status");
        if (status == default)
        {
            return null;
        }

        if (!int.TryParse(status.Value, out var statusCode))
        {
            return null;
        }

        var response = new HttpResponseMessage((HttpStatusCode)statusCode);
        List<(string Name, string Value)>? contentHeaders = null;

        foreach (var h in headers.Where(h => !h.Name.StartsWith(':')))
        {
            if (IsContentHeader(h.Name))
            {
                (contentHeaders ??= []).Add((h.Name, h.Value));
            }
            else
            {
                response.Headers.TryAddWithoutValidation(h.Name, h.Value);
            }
        }

        if (contentHeaders == null) return response;
        response.Content = new ByteArrayContent([]);
        foreach (var (name, value) in contentHeaders)
        {
            response.Content.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }

    private static bool IsContentHeader(string name) => name.ToLowerInvariant() switch
    {
        "content-type" or "content-length" or "content-encoding" or
            "content-language" or "content-location" or "content-md5" or
            "content-range" or "content-disposition" or "expires" or "last-modified" => true,
        _ => false
    };
}
