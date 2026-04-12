using System.Buffers;
using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.Http2;

/// <summary>
/// Encodes HTTP request messages as HTTP/2 frame sequences.
/// Stateful: maintains HPACK encoder and stream ID counter.
/// One instance per connection.
/// </summary>
public sealed class RequestEncoder(bool useHuffman = false, int maxFrameSize = 16384)
{
    private HpackEncoder _hpack = new(useHuffman);
    private int _maxFrameSize = maxFrameSize;
    private long _connectionSendWindow = 65535; // Tracks connection-level flow control (for RFC 9113 compliance)
    private long _initialSendStreamWindow = 65535; // RFC 9113 §6.5.2 default
    private readonly Dictionary<int, long> _streamSendWindows = new();

    // Tracks MemoryPool rentals from the previous Encode() call so they can be
    // disposed once the caller has consumed the frame list (contract: callers consume
    // frames before the next Encode() call).
    private readonly List<IMemoryOwner<byte>> _rentedBodyOwners = new(4);

    // Reused across Encode() calls to avoid List<HpackHeader> allocation per request.
    private readonly List<HpackHeader> _reusableHeaders = new(16);

    // Reused across Encode() calls to avoid List<Http2Frame> allocation per request.
    // Safe: callers consume the list immediately in a foreach before the next Encode() call.
    private readonly List<Http2Frame> _reusableFrames = new(8);

    /// <summary>
    /// Encodes a request to HTTP/2 frames. Returns the stream ID and frame list.
    /// Thread-safety: not thread-safe (one stream at a time per connection).
    /// </summary>
    public IReadOnlyList<Http2Frame> Encode(HttpRequestMessage request, int streamId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RequestUri);

        if (streamId < 0)
        {
            throw new Http2Exception("HTTP/2 stream ID space exhausted: all client stream IDs have been used.");
        }

        // Dispose MemoryPool rentals from the previous Encode() call.
        // Safe: callers consume the frame list before calling Encode() again.
        ReturnRentedBuffers();

        _reusableHeaders.Clear();
        BuildHeaderList(request, _reusableHeaders);
        ValidatePseudoHeaders(_reusableHeaders);

        using var hpackOwner = MemoryPool<byte>.Shared.Rent(4096);
        var hpackWritable = hpackOwner.Memory.Span;
        var hpackBytesWritten = _hpack.Encode(_reusableHeaders, ref hpackWritable, useHuffman);
        // Copy the written memory to an owned array so multiple Encode() calls batched
        // in the same scheduling turn (eager re-pull) do not alias each other's header
        // block data through the shared MemoryPool rental.
        var headerBlock = hpackOwner.Memory[..hpackBytesWritten].ToArray().AsMemory();
        var hasBody = request.Content != null;

        _reusableFrames.Clear();
        EncodeHeaders(_reusableFrames, streamId, headerBlock, hasBody);

        if (!hasBody)
        {
            return _reusableFrames;
        }

        // Stream body directly into MaxFrameSize-sized DATA frames using pooled buffers.
        // Single copy: content stream → rented buffer (no MemoryStream, no byte[] intermediate).
        var contentStream = request.Content!.ReadAsStream();
        var streamWindow = _streamSendWindows.GetValueOrDefault(streamId, 65535L);
        var effectiveWindow = Math.Max(0L, Math.Min(_connectionSendWindow, streamWindow));
        var bytesToSend = (int)effectiveWindow;
        var totalRead = 0;
        var dataFrameStartIndex = _reusableFrames.Count;

        while (totalRead < bytesToSend)
        {
            var maxRead = Math.Min(_maxFrameSize, bytesToSend - totalRead);
            var owner = MemoryPool<byte>.Shared.Rent(maxRead);
            _rentedBodyOwners.Add(owner);
            var bytesRead = contentStream.Read(owner.Memory.Span[..maxRead]);
            if (bytesRead == 0)
            {
                break;
            }

            _reusableFrames.Add(new DataFrame(streamId, owner, bytesRead, endStream: false));
            totalRead += bytesRead;
        }

        _connectionSendWindow -= totalRead;
        _streamSendWindows[streamId] = streamWindow - totalRead;

        // Set END_STREAM on the final DATA frame, or emit an empty one if no data was read.
        if (_reusableFrames.Count > dataFrameStartIndex)
        {
            var lastIdx = _reusableFrames.Count - 1;
            var last = (DataFrame)_reusableFrames[lastIdx];
            _reusableFrames[lastIdx] = new DataFrame(streamId, last.Data, endStream: true);
        }
        else
        {
            _reusableFrames.Add(new DataFrame(streamId, Array.Empty<byte>(), endStream: true));
        }

        return _reusableFrames;
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
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var hpackWritable = owner.Memory.Span;
        var hpackBytesWritten = _hpack.Encode(_reusableHeaders, ref hpackWritable, useHuffman);
        return owner.Memory[..hpackBytesWritten].ToArray(); // TEST ONLY: copy intentional — callers own the byte[]
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
            switch (key)
            {
                case SettingsParameter.MaxFrameSize:
                    _maxFrameSize = (int)val;
                    break;
                case SettingsParameter.HeaderTableSize:
                    _hpack.AcknowledgeTableSizeChange((int)val);
                    break;
                case SettingsParameter.InitialWindowSize:
                {
                    // RFC 9113 §6.9.2: Apply delta to all existing stream send windows
                    var delta = (long)val - _initialSendStreamWindow;
                    _initialSendStreamWindow = (long)val;
                    foreach (var streamId in _streamSendWindows.Keys)
                    {
                        _streamSendWindows[streamId] += delta;
                    }

                    break;
                }
            }
        }
    }


    /// <summary>
    /// Resets HPACK encoder state and flow-control windows for reconnect.
    /// Must be called before replaying requests on a new connection.
    /// </summary>
    public void ResetHpack()
    {
        _hpack = new HpackEncoder(useHuffman);
        _streamSendWindows.Clear();
        _connectionSendWindow = 65535;
        _initialSendStreamWindow = 65535;
    }

    /// <summary>
    /// Disposes all MemoryPool rentals from the previous Encode() call.
    /// Must be called before reusing the frame list.
    /// </summary>
    private void ReturnRentedBuffers()
    {
        for (var i = 0; i < _rentedBodyOwners.Count; i++)
        {
            _rentedBodyOwners[i].Dispose();
        }

        _rentedBodyOwners.Clear();
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