using System.Buffers;
using Akka.Actor;
using TurboHTTP.Context.Features;
using TurboHTTP.Protocol.Multiplexed.Body;
using TurboHTTP.Server;

namespace TurboHTTP.Protocol.Syntax.Http2;

/// <summary>
/// Per-stream header and body buffer management for HTTP/2.
/// Extracted from Http20ConnectionStage for independent testability.
/// </summary>
internal sealed class StreamState
{
    private readonly MemoryPool<byte> _pool = MemoryPool<byte>.Shared;

    private IMemoryOwner<byte>? _headerOwner;
    private Memory<byte> _headerBuffer;
    private int _headerLength;
    private HttpResponseMessage? _response;
    private TurboHttpRequestFeature? _requestFeature;
    private TurboHttpContext? _turboContext;
    private List<(string Name, string Value)>? _contentHeaders;
    private Dictionary<string, string>? _pseudoHeaders;
    private IBodyDecoder? _bodyDecoder;
    private IBodyEncoder? _bodyEncoder;
    private Queue<StreamBodyChunk<int>>? _outboundBuffer;

    public bool HasResponse => _response is not null;

    public bool HasContentHeaders => _contentHeaders is not null;

    public bool HasBodyDecoder => _bodyDecoder is not null;

    public bool HasBodyEncoder => _bodyEncoder is not null;

    public bool HasPendingOutbound => _outboundBuffer is { Count: > 0 };

    public bool IsBodyEncoderComplete { get; private set; }

    public bool IsRemoteClosed { get; private set; }

    public ReadOnlySpan<byte> GetHeaderSpan()
    {
        return _headerBuffer[.._headerLength].Span;
    }

    public void InitResponse(HttpResponseMessage response)
    {
        _response = response;
    }

    public HttpResponseMessage GetOrCreateResponse()
    {
        return _response ??= new HttpResponseMessage();
    }

    public HttpResponseMessage GetResponse()
    {
        return _response ?? throw new InvalidOperationException("No response has been initialized.");
    }

    public void InitRequestFeature(TurboHttpRequestFeature feature)
    {
        _requestFeature = feature;
    }

    public TurboHttpRequestFeature? GetRequestFeature() => _requestFeature;

    public void SetTurboContext(TurboHttpContext context)
    {
        _turboContext = context;
    }

    public TurboHttpContext? GetTurboContext() => _turboContext;

    public void AddPseudoHeader(string name, string value)
    {
        _pseudoHeaders ??= [];
        _pseudoHeaders[name] = value;
    }

    public string GetPseudoHeader(string name)
    {
        if (_pseudoHeaders?.TryGetValue(name, out var value) == true)
        {
            return value;
        }

        throw new InvalidOperationException($"Pseudo-header '{name}' not found.");
    }

    public void AddContentHeader(string name, string value)
    {
        _contentHeaders ??= [];
        _contentHeaders.Add((name, value));
    }

    public IReadOnlyList<(string Name, string Value)>? ContentHeaders => _contentHeaders;

    public void ApplyContentHeadersTo(HttpContent content)
    {
        if (_contentHeaders is null)
        {
            return;
        }

        foreach (var (name, value) in _contentHeaders)
        {
            content.Headers.TryAddWithoutValidation(name, value);
        }
    }

    public void InitBodyDecoder(IBodyDecoder decoder)
    {
        _bodyDecoder = decoder;
    }

    public void DetachBodyDecoder()
    {
        _bodyDecoder = null;
    }

    public void FeedBody(ReadOnlySpan<byte> data, bool endStream)
    {
        if (HasBodyDecoder)
        {
            _bodyDecoder?.Feed(data, endStream);
        }
    }

    public Stream GetBodyStream()
    {
        if (_bodyDecoder is null)
        {
            throw new InvalidOperationException("No body decoder has been initialized.");
        }

        return _bodyDecoder.GetBodyStream();
    }

    public void AbortBody()
    {
        _bodyDecoder?.Abort();
    }

    public void InitBodyEncoder(IBodyEncoder encoder)
    {
        _bodyEncoder = encoder;
    }

    public void StartBodyEncoder(Stream bodyStream, int streamId, IActorRef stageActor)
    {
        if (_bodyEncoder is null)
        {
            throw new InvalidOperationException("No body encoder has been initialized.");
        }

        _bodyEncoder.Start(bodyStream, msg =>
        {
            var tagged = msg switch
            {
                OutboundBodyChunk chunk => new StreamBodyChunk<int>(streamId, chunk.Owner, chunk.Length),
                OutboundBodyComplete => new StreamBodyComplete<int>(streamId),
                OutboundBodyFailed failed => new StreamBodyFailed<int>(streamId, failed.Reason),
                _ => msg
            };

            stageActor.Tell(tagged);
        });
    }

    public void EnqueueBodyChunk(StreamBodyChunk<int> chunk)
    {
        _outboundBuffer ??= new Queue<StreamBodyChunk<int>>();
        _outboundBuffer.Enqueue(chunk);
    }

    public void MarkBodyEncoderComplete()
    {
        IsBodyEncoderComplete = true;
    }

    public void MarkRemoteClosed()
    {
        IsRemoteClosed = true;
    }

    public bool TryDequeueBodyChunk(out StreamBodyChunk<int>? chunk)
    {
        if (_outboundBuffer is { Count: > 0 })
        {
            chunk = _outboundBuffer.Dequeue();
            return true;
        }

        chunk = null;
        return false;
    }

    public StreamBodyChunk<int>? PeekBodyChunk()
    {
        return _outboundBuffer is { Count: > 0 } ? _outboundBuffer.Peek() : null;
    }


    public void Reset()
    {
        _headerOwner?.Dispose();
        _headerOwner = null;
        _headerBuffer = default;
        _headerLength = 0;
        _response = null;
        _requestFeature = null;
        _turboContext = null;
        _contentHeaders = null;
        _pseudoHeaders = null;
        _bodyDecoder?.Dispose();
        _bodyDecoder = null;
        _bodyEncoder?.Dispose();
        _bodyEncoder = null;
        DisposeOutboundBuffer();
        _outboundBuffer = null;
        IsBodyEncoderComplete = false;
        IsRemoteClosed = false;
    }

    public void AppendHeader(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        EnsureHeaderCapacity(_headerLength + data.Length);
        data.CopyTo(_headerBuffer.Span[_headerLength..]);
        _headerLength += data.Length;
    }

    private void DisposeOutboundBuffer()
    {
        if (_outboundBuffer is null)
        {
            return;
        }

        while (_outboundBuffer.Count > 0)
        {
            _outboundBuffer.Dequeue().Owner.Dispose();
        }
    }

    private void EnsureHeaderCapacity(int required)
    {
        if (_headerOwner == null || required > _headerBuffer.Length)
        {
            RentNewHeaderBuffer(required);
        }
    }

    private void RentNewHeaderBuffer(int size)
    {
        var newOwner = _pool.Rent(size);
        if (_headerOwner != null)
        {
            _headerBuffer.Span.CopyTo(newOwner.Memory.Span);
            _headerOwner.Dispose();
        }

        _headerOwner = newOwner;
        _headerBuffer = newOwner.Memory;
    }
}