using Akka.Actor;
using TurboHTTP.Context.Features;
using TurboHTTP.Protocol.Multiplexed.Body;

namespace TurboHTTP.Protocol.Syntax.Http3;

/// <summary>
/// Unified per-stream state for HTTP/3 multiplexing (client and server).
/// Manages response/request assembly, pseudo-headers, content headers, body buffering,
/// and body encoder/decoder handling. Pooled and reused via <see cref="Reset"/>.
/// </summary>
internal sealed class StreamState
{
    private HttpResponseMessage? _response;
    private HttpRequestMessage? _request;
    private TurboHttpRequestFeature? _requestFeature;
    private List<(string Name, string Value)>? _contentHeaders;
    private Dictionary<string, string>? _pseudoHeaders;
    private IBodyDecoder? _bodyDecoder;
    private IBodyEncoder? _bodyEncoder;
    private Queue<StreamBodyChunk<long>>? _outboundBuffer;

    public long StreamId { get; private set; } = -1;

    public bool HasResponse => _response is not null;

    public bool HasRequest => _request is not null;

    public bool HasContentHeaders => _contentHeaders is not null;

    public bool HasBodyDecoder => _bodyDecoder is not null;

    public bool HasBodyEncoder => _bodyEncoder is not null;

    public bool HasPendingOutbound => _outboundBuffer is { Count: > 0 };

    public bool IsBodyEncoderComplete { get; private set; }

    public long? ExpectedContentLength { get; set; }

    public void Initialize(long streamId)
    {
        StreamId = streamId;
    }

    public HttpResponseMessage InitResponse()
    {
        _response = new HttpResponseMessage();
        return _response;
    }

    public HttpResponseMessage GetResponse()
    {
        return _response ?? throw new InvalidOperationException("No response has been initialized.");
    }

    public void InitRequest(HttpRequestMessage request)
    {
        _request = request;
    }

    public HttpRequestMessage GetRequest()
    {
        return _request ?? throw new InvalidOperationException("No request has been initialized.");
    }

    public void InitRequestFeature(TurboHttpRequestFeature feature)
    {
        _requestFeature = feature;
    }

    public TurboHttpRequestFeature? GetRequestFeature()
    {
        return _requestFeature;
    }

    public HttpRequestMessage GetOrCreateRequest()
    {
        return _request ??= new HttpRequestMessage();
    }

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

    public void FeedBody(ReadOnlySpan<byte> data, bool endStream)
    {
        if (HasBodyDecoder)
        {
            _bodyDecoder?.Feed(data, endStream);
        }
    }

    public HttpContent GetContent()
    {
        if (_bodyDecoder is null)
        {
            throw new InvalidOperationException("No body decoder has been initialized.");
        }

        return _bodyDecoder.GetContent();
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

    public void DetachBodyDecoder()
    {
        _bodyDecoder = null;
    }

    public void InitBodyEncoder(IBodyEncoder encoder)
    {
        _bodyEncoder = encoder;
    }

    public void StartBodyEncoder(HttpContent content, long streamId, IActorRef stageActor)
    {
        if (_bodyEncoder is null)
        {
            throw new InvalidOperationException("No body encoder has been initialized.");
        }

        _bodyEncoder.Start(content, msg =>
        {
            var tagged = msg switch
            {
                OutboundBodyChunk chunk => new StreamBodyChunk<long>(streamId, chunk.Owner, chunk.Length),
                OutboundBodyComplete => new StreamBodyComplete<long>(streamId),
                OutboundBodyFailed failed => new StreamBodyFailed<long>(streamId, failed.Reason),
                _ => msg
            };

            stageActor.Tell(tagged);
        });
    }

    public void StartBodyEncoder(Stream bodyStream, long streamId, IActorRef stageActor)
    {
        if (_bodyEncoder is null)
        {
            throw new InvalidOperationException("No body encoder has been initialized.");
        }

        _bodyEncoder.Start(bodyStream, msg =>
        {
            var tagged = msg switch
            {
                OutboundBodyChunk chunk => new StreamBodyChunk<long>(streamId, chunk.Owner, chunk.Length),
                OutboundBodyComplete => new StreamBodyComplete<long>(streamId),
                OutboundBodyFailed failed => new StreamBodyFailed<long>(streamId, failed.Reason),
                _ => msg
            };

            stageActor.Tell(tagged);
        });
    }

    public void EnqueueBodyChunk(StreamBodyChunk<long> chunk)
    {
        _outboundBuffer ??= new Queue<StreamBodyChunk<long>>();
        _outboundBuffer.Enqueue(chunk);
    }

    public StreamBodyChunk<long>? PeekBodyChunk()
    {
        return _outboundBuffer is { Count: > 0 } ? _outboundBuffer.Peek() : null;
    }

    public bool TryDequeueBodyChunk(out StreamBodyChunk<long>? chunk)
    {
        if (_outboundBuffer is { Count: > 0 })
        {
            chunk = _outboundBuffer.Dequeue();
            return true;
        }

        chunk = null;
        return false;
    }

    public void MarkBodyEncoderComplete()
    {
        IsBodyEncoderComplete = true;
    }

    public void Reset()
    {
        StreamId = -1;
        _response = null;
        _request = null;
        _requestFeature = null;
        ExpectedContentLength = null;
        _contentHeaders = null;
        _pseudoHeaders = null;
        _bodyDecoder?.Dispose();
        _bodyDecoder = null;
        _bodyEncoder?.Dispose();
        _bodyEncoder = null;
        DisposeOutboundBuffer();
        _outboundBuffer = null;
        IsBodyEncoderComplete = false;
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
}