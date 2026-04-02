using System.Buffers;
using TurboHttp.Protocol.Http2.Hpack;
using TurboHttp.Protocol.Semantics;

namespace TurboHttp.Protocol.Http2;

/// <summary>
/// Encodes HTTP request messages as HTTP/2 frame sequences.
/// Stateful: maintains HPACK encoder and stream ID counter.
/// One instance per connection.
/// </summary>
public sealed class Http2RequestEncoder(bool useHuffman = false, int maxFrameSize = 16384)
{
    private readonly HpackEncoder _hpack = new(useHuffman);
    private readonly bool _useHuffman = useHuffman; // passed explicitly to IBufferWriter<byte> overload
    private int _maxFrameSize = maxFrameSize;
    private long _connectionSendWindow = 65535; // Tracks connection-level flow control (for RFC 9113 compliance)
    private readonly Dictionary<int, long> _streamSendWindows = new();
    private int _nextStreamId = 1; // Client stream IDs: odd numbers starting at 1

    // Reused across Encode() calls to avoid one ArrayBufferWriter allocation per request (C3).
    private readonly ArrayBufferWriter<byte> _headerBlockWriter = new(256);

    // Reused across Encode() calls for request body buffering — avoids new MemoryStream() per request.
    private readonly MemoryStream _bodyMs = new(4096);

    // Reused across Encode() calls to avoid List<HpackHeader> allocation per request.
    private readonly List<HpackHeader> _reusableHeaders = new(16);

    // Reused across Encode() calls to avoid List<Http2Frame> allocation per request.
    // Safe: callers consume the list immediately in a foreach before the next Encode() call.
    private readonly List<Http2Frame> _reusableFrames = new(8);

    /// <summary>
    /// Encodes a request to HTTP/2 frames. Returns the stream ID and frame list.
    /// Thread-safety: not thread-safe (one stream at a time per connection).
    /// </summary>
    public (int StreamId, IReadOnlyList<Http2Frame> Frames) Encode(HttpRequestMessage request, int streamId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RequestUri);

        if (streamId < 0)
        {
            throw new Http2Exception("HTTP/2 stream ID space exhausted: all client stream IDs have been used.");
        }

        _reusableHeaders.Clear();
        BuildHeaderList(request, _reusableHeaders);
        ValidatePseudoHeaders(_reusableHeaders);

        _headerBlockWriter.Clear();
        _hpack.Encode(_reusableHeaders, _headerBlockWriter, _useHuffman);
        // Copy the written memory to an owned array so multiple Encode() calls batched
        // in the same scheduling turn (eager re-pull) do not alias each other's header
        // block data through the shared _headerBlockWriter internal buffer.
        var headerBlock = _headerBlockWriter.WrittenMemory.ToArray().AsMemory();
        var hasBody = request.Content != null;

        _reusableFrames.Clear();
        var frames = _reusableFrames;
        EncodeHeaders(frames, streamId, headerBlock, hasBody);

        if (!hasBody)
        {
            return (streamId, frames);
        }

        _bodyMs.Position = 0;
        _bodyMs.SetLength(0);
        request.Content!.CopyTo(_bodyMs, null, new CancellationToken(false));
        _bodyMs.TryGetBuffer(out var bodySegment);
        var bodyLen = (int)_bodyMs.Length;
        var bodyOwned = new byte[bodyLen];
        bodySegment.AsMemory()[..bodyLen].CopyTo(bodyOwned);
        var body = (ReadOnlyMemory<byte>)bodyOwned;
        if (body.Length > 0)
        {
            var streamWindow = _streamSendWindows.GetValueOrDefault(streamId, 65535L);
            var effectiveWindow = Math.Max(0L, Math.Min(_connectionSendWindow, streamWindow));
            var bytesToSend = (int)Math.Min(body.Length, effectiveWindow);

            _connectionSendWindow -= bytesToSend;
            _streamSendWindows[streamId] = streamWindow - bytesToSend;

            if (bytesToSend == 0)
            {
                frames.Add(new DataFrame(streamId, Array.Empty<byte>(), endStream: true));
            }
            else
            {
                var offset = 0;
                while (offset < bytesToSend)
                {
                    var chunkSize = Math.Min(bytesToSend - offset, _maxFrameSize);
                    var isLast = offset + chunkSize >= bytesToSend;
                    frames.Add(
                        new DataFrame(streamId, body[offset..(offset + chunkSize)], endStream: isLast));
                    offset += chunkSize;
                }
            }
        }
        else
        {
            frames.Add(new DataFrame(streamId, Array.Empty<byte>(), endStream: true));
        }

        return (streamId, frames);
    }

    /// <summary>
    /// TEST ONLY: Encodes a request into a buffer and returns stream ID and bytes written.
    /// Span-based API for compatibility with test code that needs buffer control.
    /// </summary>
    internal (int StreamId, int BytesWritten) Encode(HttpRequestMessage request, ref Memory<byte> buffer)
    {
        ArgumentNullException.ThrowIfNull(request);

        var streamId = AllocateStreamId();
        var (_, frames) = Encode(request, streamId);
        var totalWritten = 0;

        foreach (var frame in frames)
        {
            var frameSize = frame.SerializedSize;
            if (buffer.Length < frameSize)
            {
                throw new InvalidOperationException($"Buffer too small: need {frameSize} bytes, have {buffer.Length}");
            }

            var span = buffer.Span;
            frame.WriteTo(ref span);
            totalWritten += frameSize;
            buffer = buffer[frameSize..];
        }

        return (streamId, totalWritten);
    }

    /// <summary>
    /// TEST ONLY: Encodes a request and extracts the raw HPACK header block.
    /// Used by RFC compliance tests to verify header encoding details.
    /// </summary>
    internal byte[] EncodeToHpackBlock(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RequestUri);

        _reusableHeaders.Clear();
        BuildHeaderList(request, _reusableHeaders);
        ValidatePseudoHeaders(_reusableHeaders);
        _headerBlockWriter.Clear();
        _hpack.Encode(_reusableHeaders, _headerBlockWriter, _useHuffman);
        return _headerBlockWriter.WrittenMemory.ToArray(); // TEST ONLY: copy intentional — callers own the byte[]
    }

    private void EncodeHeaders(List<Http2Frame> frames, int streamId, ReadOnlyMemory<byte> headerBlock, bool hasBody)
    {
        if (headerBlock.Length <= _maxFrameSize)
        {
            frames.Add(new HeadersFrame(streamId, headerBlock, endStream: !hasBody, endHeaders: true));
            return;
        }

        // Fragmented header block — first chunk goes in HEADERS frame
        frames.Add(new HeadersFrame(streamId, headerBlock[.._maxFrameSize], endStream: false,
            endHeaders: false));

        var pos = _maxFrameSize;
        while (pos < headerBlock.Length)
        {
            var chunkSize = Math.Min(headerBlock.Length - pos, _maxFrameSize);
            var isLast = pos + chunkSize >= headerBlock.Length;
            frames.Add(new ContinuationFrame(streamId, headerBlock[pos..(pos + chunkSize)],
                endHeaders: isLast));
            pos += chunkSize;
        }
    }

    private static void BuildHeaderList(HttpRequestMessage request, List<HpackHeader> headers)
    {
        var uri = request.RequestUri!;
        var pathAndQuery = string.IsNullOrEmpty(uri.Query)
            ? uri.AbsolutePath
            : uri.AbsolutePath + uri.Query;

        headers.Add(new(":method", request.Method.Method));
        headers.Add(new(":path", pathAndQuery));
        headers.Add(new(":scheme", uri.Scheme));
        headers.Add(new(":authority", UriSanitizer.FormatAuthority(uri)));

        // I1: foreach instead of LINQ to eliminate Where+Select iterator allocations
        foreach (var h in request.Headers)
        {
            if (!IsForbidden(h.Key))
            {
                headers.Add(new HpackHeader(ToLower(h.Key), JoinValues(h.Value)));
            }
        }

        if (request.Content == null)
        {
            return;
        }

        foreach (var h in request.Content.Headers)
        {
            headers.Add(new HpackHeader(ToLower(h.Key), JoinValues(h.Value)));
        }
    }

    /// <summary>
    /// Validates pseudo-headers per RFC 9113 §8.3.1:
    /// - All four required: :method, :path, :scheme, :authority
    /// - Must appear before regular headers
    /// - Must have exactly one of each (no duplicates)
    /// - No other pseudo-headers allowed
    /// </summary>
    internal static void ValidatePseudoHeaders(List<HpackHeader> headers)
    {
        var hasMethod = false;
        var hasPath = false;
        var hasScheme = false;
        var hasAuthority = false;
        var lastPseudoIndex = -1;
        var firstRegularIndex = int.MaxValue;

        for (var i = 0; i < headers.Count; i++)
        {
            var name = headers[i].Name;

            if (name.StartsWith(':'))
            {
                lastPseudoIndex = i;

                switch (name)
                {
                    case ":method":
                        if (hasMethod)
                        {
                            throw new Http2Exception("RFC 9113 §8.3.1: Duplicate :method pseudo-header");
                        }

                        hasMethod = true;
                        break;
                    case ":path":
                        if (hasPath)
                        {
                            throw new Http2Exception("RFC 9113 §8.3.1: Duplicate :path pseudo-header");
                        }

                        hasPath = true;
                        break;
                    case ":scheme":
                        if (hasScheme)
                        {
                            throw new Http2Exception("RFC 9113 §8.3.1: Duplicate :scheme pseudo-header");
                        }

                        hasScheme = true;
                        break;
                    case ":authority":
                        if (hasAuthority)
                        {
                            throw new Http2Exception("RFC 9113 §8.3.1: Duplicate :authority pseudo-header");
                        }

                        hasAuthority = true;
                        break;
                    default:
                    {
                        throw new Http2Exception($"RFC 9113 §8.3.1: Unknown request pseudo-header '{name}'");
                    }
                }
            }
            else
            {
                if (firstRegularIndex == int.MaxValue)
                {
                    firstRegularIndex = i;
                }
            }
        }

        if (lastPseudoIndex > firstRegularIndex)
        {
            throw new Http2Exception(
                $"RFC 9113 §8.3.1: Pseudo-header at index {lastPseudoIndex} appears after regular header at index {firstRegularIndex}");
        }

        var missing = new System.Text.StringBuilder();
        if (!hasMethod)
        {
            missing.Append(missing.Length > 0 ? ", :method" : ":method");
        }

        if (!hasPath)
        {
            missing.Append(missing.Length > 0 ? ", :path" : ":path");
        }

        if (!hasScheme)
        {
            missing.Append(missing.Length > 0 ? ", :scheme" : ":scheme");
        }

        if (!hasAuthority)
        {
            missing.Append(missing.Length > 0 ? ", :authority" : ":authority");
        }

        if (missing.Length > 0)
        {
            throw new Http2Exception($"RFC 9113 §8.3.1: Missing required pseudo-headers: {missing}");
        }
    }

    /// <summary>
    /// Updates the connection-level send window when server sends WINDOW_UPDATE on stream 0.
    /// RFC 9113 §6.9: Sender increases window size via WINDOW_UPDATE.
    /// </summary>
    public void UpdateConnectionWindow(int increment)
    {
        if (increment is < 1 or > 0x7FFFFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(increment));
        }

        _connectionSendWindow += increment;
    }

    /// <summary>
    /// Updates the stream-level send window when server sends WINDOW_UPDATE on a stream.
    /// RFC 9113 §6.9: Sender increases stream window size via WINDOW_UPDATE.
    /// </summary>
    public void UpdateStreamWindow(int streamId, int increment)
    {
        if (increment is < 1 or > 0x7FFFFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(increment));
        }

        _streamSendWindows.TryGetValue(streamId, out var current);
        _streamSendWindows[streamId] = current + increment;
    }

    /// <summary>
    /// Applies server settings to the encoder (e.g., MAX_FRAME_SIZE).
    /// RFC 9113 §6.5: Received SETTINGS ACK updates encoder state.
    /// </summary>
    public void ApplyServerSettings(IEnumerable<(SettingsParameter Key, uint Value)> settings)
    {
        foreach (var (key, val) in settings)
        {
            if (key == SettingsParameter.MaxFrameSize)
            {
                _maxFrameSize = (int)val;
            }
        }
    }


    /// <summary>
    /// Allocates the next client stream ID (odd numbers: 1, 3, 5, ...).
    /// Used by test-only overloads that do not receive an explicit stream ID.
    /// </summary>
    private int AllocateStreamId()
    {
        var id = _nextStreamId;
        _nextStreamId += 2;
        return id;
    }

    // Forbidden connection-specific headers per RFC 9113 §8.2.2
    private static bool IsForbidden(string name) =>
        string.Equals(name, "connection", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "transfer-encoding", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "upgrade", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "proxy-connection", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "keep-alive", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "te", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the lowercase version of a header name without allocating if already lowercase.
    /// HTTP header names from .NET's HttpRequestHeaders are typically already lowercase ASCII.
    /// </summary>
    private static string ToLower(string name)
    {
        foreach (var c in name)
        {
            if (c is >= 'A' and <= 'Z')
            {
                return name.ToLowerInvariant();
            }
        }

        return name;
    }

    /// <summary>
    /// Joins header values without allocating if there is only a single value (common case).
    /// </summary>
    private static string JoinValues(IEnumerable<string> values)
    {
        string? first = null;
        foreach (var v in values)
        {
            if (first is null)
            {
                first = v;
                continue;
            }

            // Multiple values — fall back to Join
            return string.Join(", ", values);
        }

        return first ?? string.Empty;
    }
}