using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;

namespace TurboHttp.Protocol;

/// <summary>
/// Encodes HTTP request messages as HTTP/2 frame sequences.
/// Stateful: maintains HPACK encoder and stream ID counter.
/// One instance per connection.
/// </summary>
public sealed class Http2RequestEncoder(bool useHuffman = false, int maxFrameSize = 16384)
{
    private readonly HpackEncoder _hpack = new(useHuffman);
    private int _maxFrameSize = maxFrameSize;
    private long _connectionSendWindow = 65535; // Tracks connection-level flow control (for RFC 7540 compliance)
    private readonly Dictionary<int, long> _streamSendWindows = new();
    private int _nextStreamId = 1; // Client stream IDs: odd numbers starting at 1

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

        var headers = BuildHeaderList(request);
        ValidatePseudoHeaders(headers);

        var headerBlock = _hpack.Encode(headers).ToArray();
        var hasBody = request.Content != null;

        var frames = new List<Http2Frame>();
        EncodeHeaders(frames, streamId, headerBlock, hasBody);

        if (!hasBody)
        {
            return (streamId, frames);
        }

        using var ms = new MemoryStream();
        request.Content!.CopyTo(ms, null, new CancellationToken(false));
        var body = ms.ToArray();
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
                        new DataFrame(streamId, body.AsMemory()[offset..(offset + chunkSize)], endStream: isLast));
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
    /// TEST ONLY: Encodes a request and returns serialized bytes with total size.
    /// Used by integration tests that need to compare frame sizes for compression verification.
    /// </summary>
    internal (int StreamId, int BytesWritten) EncodeToBytes(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var streamId = AllocateStreamId();
        var (sid, frames) = Encode(request, streamId);
        var totalSize = frames.Sum(f => f.SerializedSize);
        return (sid, totalSize);
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

        var headers = BuildHeaderList(request);
        ValidatePseudoHeaders(headers);
        return _hpack.Encode(headers).ToArray();
    }

    private void EncodeHeaders(List<Http2Frame> frames, int streamId, byte[] headerBlock, bool hasBody)
    {
        if (headerBlock.Length <= _maxFrameSize)
        {
            frames.Add(new HeadersFrame(streamId, headerBlock, endStream: !hasBody, endHeaders: true));
            return;
        }

        // Fragmented header block — first chunk goes in HEADERS frame
        frames.Add(new HeadersFrame(streamId, headerBlock.AsMemory()[.._maxFrameSize], endStream: false,
            endHeaders: false));

        var pos = _maxFrameSize;
        while (pos < headerBlock.Length)
        {
            var chunkSize = Math.Min(headerBlock.Length - pos, _maxFrameSize);
            var isLast = pos + chunkSize >= headerBlock.Length;
            frames.Add(new ContinuationFrame(streamId, headerBlock.AsMemory()[pos..(pos + chunkSize)],
                endHeaders: isLast));
            pos += chunkSize;
        }
    }

    private static void AppendEndStream(List<Http2Frame> frames, int streamId)
    {
        // RFC 9113 §6.1 — zero-length DATA with END_STREAM is the correct approach
        // when the last header frame is a CONTINUATION (which has no END_STREAM flag)
        if (frames.Count == 0)
        {
            return;
        }

        if (frames[^1] is HeadersFrame hf)
        {
            frames[^1] = new HeadersFrame(
                hf.StreamId,
                hf.HeaderBlockFragment,
                endStream: true,
                endHeaders: hf.EndHeaders);
        }
        else
        {
            // Last frame is ContinuationFrame — append zero-length DATA with END_STREAM
            frames.Add(new DataFrame(streamId, Array.Empty<byte>(), endStream: true));
        }
    }

    // ── Header building (mirrors Http2RequestEncoder pseudo-header logic) ─────────────

    private static List<(string, string)> BuildHeaderList(HttpRequestMessage request)
    {
        var uri = request.RequestUri!;
        var pathAndQuery = string.IsNullOrEmpty(uri.Query)
            ? uri.AbsolutePath
            : uri.AbsolutePath + uri.Query;

        var headers = new List<(string, string)>
        {
            (":method", request.Method.Method),
            (":path", pathAndQuery),
            (":scheme", uri.Scheme),
            (":authority", uri.Authority),
        };

        headers.AddRange(request.Headers.Where(x => !IsForbidden(x.Key))
            .Select(h => (h.Key.ToLowerInvariant(), string.Join(", ", h.Value))));

        if (request.Content == null) return headers;

        headers.AddRange(request.Content.Headers.Select(h => (h.Key.ToLowerInvariant(), string.Join(", ", h.Value))));

        return headers;
    }

    // ── Pseudo-Header Validation (RFC 7540 §8.1.2.1) ──────────────────────────

    /// <summary>
    /// Validates pseudo-headers per RFC 7540 §8.1.2.1:
    /// - All four required: :method, :path, :scheme, :authority
    /// - Must appear before regular headers
    /// - Must have exactly one of each (no duplicates)
    /// - No other pseudo-headers allowed
    /// </summary>
    internal static void ValidatePseudoHeaders(List<(string Name, string Value)> headers)
    {
        var hasMethod = false;
        var hasPath = false;
        var hasScheme = false;
        var hasAuthority = false;
        var lastPseudoIndex = -1;
        var firstRegularIndex = int.MaxValue;

        for (var i = 0; i < headers.Count; i++)
        {
            var (name, _) = headers[i];

            if (name.StartsWith(':'))
            {
                lastPseudoIndex = i;

                switch (name)
                {
                    case ":method":
                        if (hasMethod)
                        {
                            throw new Http2Exception("RFC 7540 §8.1.2.1: Duplicate :method pseudo-header");
                        }

                        hasMethod = true;
                        break;
                    case ":path":
                        if (hasPath)
                        {
                            throw new Http2Exception("RFC 7540 §8.1.2.1: Duplicate :path pseudo-header");
                        }

                        hasPath = true;
                        break;
                    case ":scheme":
                        if (hasScheme)
                        {
                            throw new Http2Exception("RFC 7540 §8.1.2.1: Duplicate :scheme pseudo-header");
                        }

                        hasScheme = true;
                        break;
                    case ":authority":
                        if (hasAuthority)
                        {
                            throw new Http2Exception("RFC 7540 §8.1.2.1: Duplicate :authority pseudo-header");
                        }

                        hasAuthority = true;
                        break;
                    default:
                    {
                        throw new Http2Exception($"RFC 7540 §8.1.2.1: Unknown request pseudo-header '{name}'");
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
                $"RFC 7540 §8.1.2.1: Pseudo-header at index {lastPseudoIndex} appears after regular header at index {firstRegularIndex}");
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
            throw new Http2Exception($"RFC 7540 §8.1.2.1: Missing required pseudo-headers: {missing}");
        }
    }

    // ── Flow Control Window Management ────────────────────────────────────────

    /// <summary>
    /// Updates the connection-level send window when server sends WINDOW_UPDATE on stream 0.
    /// RFC 7540 §6.9: Sender increases window size via WINDOW_UPDATE.
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
    /// RFC 7540 §6.9: Sender increases stream window size via WINDOW_UPDATE.
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
    /// RFC 7540 §6.5: Received SETTINGS ACK updates encoder state.
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

    private void CopyBodyToFrames(Stream bodyStream, int streamId, List<Http2Frame> frames)
    {
        var pool = MemoryPool<byte>.Shared;
        using var owner = pool.Rent(_maxFrameSize);
        var buffer = owner.Memory;

        var isEndOfStream = false;

        while (!isEndOfStream)
        {
            var streamWindow = _streamSendWindows.GetValueOrDefault(streamId, 65535L);
            var effectiveWindow = Math.Max(0L, Math.Min(_connectionSendWindow, streamWindow));
            if (effectiveWindow <= 0)
            {
                frames.Add(new DataFrame(streamId, ReadOnlyMemory<byte>.Empty, endStream: true));
                return;
            }

            var toRead = (int)Math.Min(buffer.Length, effectiveWindow);
            var read = bodyStream.Read(buffer.Span[..toRead]);

            if (read == 0)
            {
                isEndOfStream = true;
                if (frames.Count == 0 || frames[^1] is not DataFrame)
                {
                    frames.Add(new DataFrame(streamId, ReadOnlyMemory<byte>.Empty, endStream: true));
                }
                else
                {
                    var lastFrame = (DataFrame)frames[^1];
                    frames[^1] = new DataFrame(lastFrame.StreamId, lastFrame.Data, endStream: true);
                }

                continue;
            }

            _connectionSendWindow -= read;
            _streamSendWindows[streamId] = streamWindow - read;

            var remaining = read;
            var offset = 0;

            while (remaining > 0)
            {
                var chunkSize = Math.Min(remaining, _maxFrameSize);
                var slice = buffer.Slice(offset, chunkSize);

                frames.Add(new DataFrame(streamId, slice, endStream: false));

                offset += chunkSize;
                remaining -= chunkSize;
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
}