using TurboHTTP.Protocol.LineBased;
using TurboHTTP.Protocol.LineBased.Body;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http11.Options;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Protocol.Syntax.Http11.Server;

internal sealed class Http11ServerDecoder
{
    private enum Phase
    {
        RequestLine,
        Headers,
        Body,
        Done
    }

    private readonly Http11ServerDecoderOptions _options;
    private readonly HeaderBlockReader _headerReader;

    private Phase _phase = Phase.RequestLine;
    private HttpMethod _method = null!;
    private string _target = null!;
    private Version _version = null!;
    private IBodyDecoder? _bodyDecoder;

    public Http11ServerDecoder(Http11ServerDecoderOptions options)
    {
        options.Validate();
        _options = options;
        var s = options.Shared;
        _headerReader =
            new HeaderBlockReader(s.MaxHeaderBytes, s.MaxHeaderCount, s.HeaderLineMaxLength, s.AllowObsFold);
    }

    public IBodyDecoder? CurrentBodyDecoder => _bodyDecoder;

    public DecodeOutcome Feed(ReadOnlySpan<byte> data, out int consumed)
    {
        consumed = 0;
        var pos = 0;

        if (_phase == Phase.RequestLine)
        {
            if (!RequestLineParser.TryParse(data, _options.Shared.RequestLineMaxLength, out var method, out var target, out var version, out var rlConsumed))
            {
                return DecodeOutcome.NeedMore;
            }

            _method = method;
            _target = target;
            _version = version;
            pos = rlConsumed.Value;
            _phase = Phase.Headers;
        }

        if (_phase == Phase.Headers)
        {
            var result = _headerReader.Feed(data[pos..], out var hConsumed);
            pos += hConsumed;
            if (result == HeaderBlockResult.NeedMore)
            {
                consumed = pos;
                return DecodeOutcome.NeedMore;
            }

            var classification = BodySemantics.ClassifyRequest(_method, _headerReader.GetHeaders(), _version);
            _bodyDecoder = BodyDecoderFactory.Create(
                classification,
                _options.Shared.StreamingThreshold,
                _options.Shared.BufferPool,
                _options.Shared.MaxBufferedBodySize,
                _options.Shared.MaxStreamedBodySize);
            _phase = Phase.Body;
        }

        if (_phase == Phase.Body)
        {
            var done = _bodyDecoder!.Feed(data[pos..], out var bConsumed);
            pos += bConsumed;
            consumed = pos;
            if (done)
            {
                _phase = Phase.Done;
                return DecodeOutcome.Complete;
            }

            return DecodeOutcome.NeedMore;
        }

        consumed = pos;
        return DecodeOutcome.Complete;
    }

    public bool HasConnectionClose
    {
        get
        {
            foreach (var v in _headerReader.GetHeaders().GetValues(WellKnownHeaders.Connection))
            {
                if (string.Equals(v, WellKnownHeaders.CloseValue, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }

    public TurboHttpRequestFeature GetRequestFeature()
    {
        var body = _bodyDecoder?.GetBodyStream() ?? Stream.Null;

        var feature = new TurboHttpRequestFeature
        {
            Protocol = _version switch
            {
                { Major: 1, Minor: 0 } => "HTTP/1.0",
                { Major: 1, Minor: 1 } => "HTTP/1.1",
                _ => "HTTP/1.1"
            },
            Method = _method.Method,
            Path = ParsePath(_target),
            QueryString = ParseQueryString(_target),
            RawTarget = _target,
            Body = body,
        };

        // Populate directly into the feature's header dictionary, avoiding a throwaway
        // HeaderDictionary allocation plus the copy loop in the Headers setter.
        HeaderRouter.ApplyToHeaderDictionary(feature.Headers, _headerReader.GetHeaders());
        return feature;
    }

    private static string ParsePath(string target)
    {
        var queryIdx = target.IndexOf('?');
        var pathPart = queryIdx >= 0 ? target[..queryIdx] : target;
        return string.IsNullOrEmpty(pathPart) ? "/" : pathPart;
    }

    private static string ParseQueryString(string target)
    {
        var queryIdx = target.IndexOf('?');
        return queryIdx >= 0 ? target[queryIdx..] : string.Empty;
    }

    public void Reset()
    {
        _phase = Phase.RequestLine;
        _method = null!;
        _target = null!;
        _version = null!;
        _bodyDecoder = null;
        _headerReader.Reset();
    }
}