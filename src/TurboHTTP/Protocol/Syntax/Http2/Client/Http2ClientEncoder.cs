using System.Buffers;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.Syntax.Http2.Client;

/// <summary>
/// Encodes HTTP request messages as HTTP/2 frame sequences.
/// Stateful: maintains HPACK encoder and stream ID counter.
/// One instance per connection.
/// </summary>
internal sealed class Http2ClientEncoder(bool useHuffman = false, int maxFrameSize = 16384)
{
    private HpackEncoder _hpack = new(useHuffman);
    private int _maxFrameSize = maxFrameSize;

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
            throw new HttpProtocolException("HTTP/2 stream ID space exhausted: all client stream IDs have been used.");
        }

        // Dispose MemoryPool rentals from the previous Encode() call.
        // Safe: callers consume the frame list before calling Encode() again.
        ReturnRentedBuffers();

        _reusableHeaders.Clear();
        BuildHeaderList(request, _reusableHeaders);
        ValidatePseudoHeaders(_reusableHeaders);

        var hpackOwner = MemoryPool<byte>.Shared.Rent(4096);
        _rentedBodyOwners.Add(hpackOwner);
        var hpackWritable = hpackOwner.Memory.Span;
        var hpackBytesWritten = _hpack.Encode(_reusableHeaders, ref hpackWritable, useHuffman);
        var headerBlock = hpackOwner.Memory[..hpackBytesWritten];
        var hasBody = request.Content != null;

        _reusableFrames.Clear();
        EncodeHeaders(_reusableFrames, streamId, headerBlock, hasBody);

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
            : string.Concat(uri.AbsolutePath, uri.Query);

        headers.Add(new HpackHeader(WellKnownHeaders.Method, request.Method.Method));
        headers.Add(new HpackHeader(WellKnownHeaders.Path, pathAndQuery));
        headers.Add(new HpackHeader(WellKnownHeaders.Scheme, uri.Scheme));
        headers.Add(new HpackHeader(WellKnownHeaders.Authority, UriSanitizer.FormatAuthority(uri)));

        foreach (var h in request.Headers)
        {
            if (!ContentHeaderClassifier.IsForbiddenConnectionHeader(h.Key))
            {
                headers.Add(new HpackHeader(ContentHeaderClassifier.ToLowerAscii(h.Key), ContentHeaderClassifier.JoinHeaderValues(h.Value)));
            }
        }

        if (request.Content == null)
        {
            return;
        }

        foreach (var h in request.Content.Headers)
        {
            headers.Add(new HpackHeader(ContentHeaderClassifier.ToLowerAscii(h.Key), ContentHeaderClassifier.JoinHeaderValues(h.Value)));
        }
    }

    internal static void ValidatePseudoHeaders(List<HpackHeader> headers) =>
        PseudoHeaderValidator.ValidateRequestPseudoHeaders(
            headers,
            static h => h.Name,
            static h => h.Value,
            "RFC 9113 §8.3.1");

    /// <summary>
    /// Applies server settings to the encoder (e.g., MAX_FRAME_SIZE).
    /// RFC 9113 §6.5: Received SETTINGS ACK updates encoder state.
    /// Note: Flow control window updates (InitialWindowSize) are handled by IFlowController.
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
            }
        }
    }

    /// <summary>
    /// Resets HPACK encoder state for reconnect.
    /// Must be called before replaying requests on a new connection.
    /// </summary>
    public void ResetHpack()
    {
        _hpack = new HpackEncoder(useHuffman);
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
}