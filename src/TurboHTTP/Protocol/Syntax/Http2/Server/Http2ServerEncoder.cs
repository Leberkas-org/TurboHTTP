using System.Buffers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;

namespace TurboHTTP.Protocol.Syntax.Http2.Server;

/// <summary>
/// Encodes HTTP/2 response messages into HEADERS frame sequences.
/// Header-only encoder: response body streaming is handled by Http2ServerStateMachine via ResponseBodyHandle.
/// Stateful: maintains HPACK encoder for connection lifetime.
/// </summary>
internal sealed class Http2ServerEncoder
{
    private HpackEncoder _hpack = new(useHuffman: true);

    // Reused across Encode() calls to avoid List<HpackHeader> allocation per response
    private readonly List<HpackHeader> _reusableHeaders = new(16);

    // Reused across Encode() calls to avoid List<Http2Frame> allocation per response
    private readonly List<Http2Frame> _reusableFrames = new(8);

    // Tracks MemoryPool rentals from the previous EncodeHeaders() call
    private readonly List<IMemoryOwner<byte>> _rentedBodyOwners = new(4);

    public int MaxFrameSize { get; private set; } = 16 * 1024;

    private void EncodeHeaderFrames(List<Http2Frame> frames, int streamId, ReadOnlyMemory<byte> headerBlock,
        bool endStream)
    {
        if (headerBlock.Length <= MaxFrameSize)
        {
            frames.Add(new HeadersFrame(streamId, headerBlock, endStream: endStream, endHeaders: true));
            return;
        }

        // Fragmented header block
        frames.Add(new HeadersFrame(streamId, headerBlock[..MaxFrameSize], endStream: false, endHeaders: false));

        var pos = MaxFrameSize;
        while (pos < headerBlock.Length)
        {
            var chunkSize = Math.Min(headerBlock.Length - pos, MaxFrameSize);
            var isLast = pos + chunkSize >= headerBlock.Length;
            frames.Add(new ContinuationFrame(streamId, headerBlock[pos..(pos + chunkSize)], endHeaders: isLast));
            pos += chunkSize;
        }
    }

    public IReadOnlyList<Http2Frame> EncodeHeaders(IFeatureCollection features, int streamId, bool hasBody)
    {
        ArgumentNullException.ThrowIfNull(features);

        if (streamId < 0)
        {
            throw new HttpProtocolException("HTTP/2 stream ID space exhausted: all server stream IDs have been used.");
        }

        ReturnRentedBuffers();

        _reusableHeaders.Clear();
        BuildHeaderList(features, _reusableHeaders);

        var hpackOwner = MemoryPool<byte>.Shared.Rent(4096);
        _rentedBodyOwners.Add(hpackOwner);
        var hpackWritable = hpackOwner.Memory.Span;
        var hpackBytesWritten = _hpack.Encode(_reusableHeaders, ref hpackWritable, useHuffman: true);
        var headerBlock = hpackOwner.Memory[..hpackBytesWritten];

        _reusableFrames.Clear();
        EncodeHeaderFrames(_reusableFrames, streamId, headerBlock, endStream: !hasBody);

        return _reusableFrames;
    }

    private static void BuildHeaderList(IFeatureCollection features, List<HpackHeader> headers)
    {
        // RFC 9113 §7.2: :status pseudo-header (required)
        var responseFeature = features.Get<IHttpResponseFeature>();
        var statusCode = responseFeature?.StatusCode ?? 500;
        headers.Add(new HpackHeader(WellKnownHeaders.Status,
            WellKnownHeaders.GetStatusCodeString(statusCode)));

        // Add regular headers
        var responseHeaders = responseFeature?.Headers;
        if (responseHeaders is not null)
        {
            foreach (var h in responseHeaders)
            {
                if (ContentHeaderClassifier.IsForbiddenConnectionHeader(h.Key))
                {
                    continue;
                }

                var value = h.Value.Count == 1 ? h.Value[0]! : string.Join(", ", h.Value);
                headers.Add(new HpackHeader(ContentHeaderClassifier.ToLowerAscii(h.Key), value));
            }
        }
    }

    /// <summary>
    /// Applies client settings to the encoder (e.g., MAX_FRAME_SIZE, HEADER_TABLE_SIZE).
    /// RFC 9113 §6.5: Received SETTINGS ACK updates encoder state.
    /// </summary>
    public void ApplyClientSettings(IEnumerable<(SettingsParameter Key, uint Value)> settings)
    {
        foreach (var (key, val) in settings)
        {
            switch (key)
            {
                case SettingsParameter.MaxFrameSize:
                    MaxFrameSize = (int)val;
                    break;
                case SettingsParameter.HeaderTableSize:
                    _hpack.AcknowledgeTableSizeChange((int)val);
                    break;
            }
        }
    }

    /// <summary>
    /// Encodes trailer headers into HTTP/2 HEADERS frame(s).
    /// RFC 9113 §8.1: Trailers are sent as a HEADERS frame with END_STREAM.
    /// RFC 9110 §6.5.1: Filters prohibited trailer fields (transfer-encoding, content-length, etc.).
    /// </summary>
    public IReadOnlyList<Http2Frame> EncodeTrailers(int streamId, IHeaderDictionary trailers)
    {
        ArgumentNullException.ThrowIfNull(trailers);

        ReturnRentedBuffers();

        _reusableHeaders.Clear();

        foreach (var header in trailers)
        {
            if (TrailerFieldValidator.IsAllowedInTrailer(header.Key))
            {
                _reusableHeaders.Add(new HpackHeader(ContentHeaderClassifier.ToLowerAscii(header.Key),
                    header.Value.ToString() ?? string.Empty));
            }
        }

        if (_reusableHeaders.Count == 0)
        {
            return Array.Empty<Http2Frame>();
        }

        var hpackOwner = MemoryPool<byte>.Shared.Rent(4096);
        _rentedBodyOwners.Add(hpackOwner);
        var hpackWritable = hpackOwner.Memory.Span;
        var hpackBytesWritten = _hpack.Encode(_reusableHeaders, ref hpackWritable, useHuffman: true);
        var headerBlock = hpackOwner.Memory[..hpackBytesWritten];

        _reusableFrames.Clear();
        EncodeHeaderFrames(_reusableFrames, streamId, headerBlock, endStream: true);

        return _reusableFrames;
    }

    /// <summary>
    /// Resets HPACK encoder state for reconnect.
    /// </summary>
    public void ResetHpack()
    {
        _hpack = new HpackEncoder(useHuffman: true);
    }

    private void ReturnRentedBuffers()
    {
        for (var i = 0; i < _rentedBodyOwners.Count; i++)
        {
            _rentedBodyOwners[i].Dispose();
        }

        _rentedBodyOwners.Clear();
    }
}