using System.Buffers;
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

    public int MaxFrameSize { get; private set; } = 16384;

    /// <summary>
    /// Encodes response headers into HEADERS and optional CONTINUATION frames.
    /// </summary>
    public IReadOnlyList<Http2Frame> EncodeHeaders(HttpResponseMessage response, int streamId, bool hasBody)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (streamId < 0)
        {
            throw new HttpProtocolException("HTTP/2 stream ID space exhausted: all server stream IDs have been used.");
        }

        ReturnRentedBuffers();

        _reusableHeaders.Clear();
        BuildHeaderList(response, _reusableHeaders);

        var hpackOwner = MemoryPool<byte>.Shared.Rent(4096);
        _rentedBodyOwners.Add(hpackOwner);
        var hpackWritable = hpackOwner.Memory.Span;
        var hpackBytesWritten = _hpack.Encode(_reusableHeaders, ref hpackWritable, useHuffman: true);
        var headerBlock = hpackOwner.Memory[..hpackBytesWritten];

        _reusableFrames.Clear();
        EncodeHeaderFrames(_reusableFrames, streamId, headerBlock, endStream: !hasBody);

        return _reusableFrames;
    }

    /// <summary>
    /// TEST ONLY: Encodes a response and extracts the raw HPACK header block.
    /// </summary>
    internal byte[] EncodeToHpackBlock(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        _reusableHeaders.Clear();
        BuildHeaderList(response, _reusableHeaders);
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var hpackWritable = owner.Memory.Span;
        var hpackBytesWritten = _hpack.Encode(_reusableHeaders, ref hpackWritable, useHuffman: true);
        return owner.Memory[..hpackBytesWritten].ToArray();
    }

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

    private static void BuildHeaderList(HttpResponseMessage response, List<HpackHeader> headers)
    {
        // RFC 9113 §7.2: :status pseudo-header (required)
        headers.Add(new HpackHeader(WellKnownHeaders.Status, WellKnownHeaders.GetStatusCodeString((int)response.StatusCode)));

        // Add regular headers
        foreach (var h in response.Headers)
        {
            if (!ContentHeaderClassifier.IsForbiddenConnectionHeader(h.Key))
            {
                headers.Add(new HpackHeader(ContentHeaderClassifier.ToLowerAscii(h.Key), ContentHeaderClassifier.JoinHeaderValues(h.Value)));
            }
        }

        // Add content headers
        if (response.Content != null)
        {
            foreach (var h in response.Content.Headers)
            {
                headers.Add(new HpackHeader(ContentHeaderClassifier.ToLowerAscii(h.Key), ContentHeaderClassifier.JoinHeaderValues(h.Value)));
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