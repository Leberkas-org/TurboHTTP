using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Protocol.Syntax.Http3.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Server;

public sealed class ServerRequestDecoderSpec
{
    private readonly QpackTableSync _encoderTableSync = new(encoderMaxCapacity: 4096, decoderMaxCapacity: 4096);
    private readonly QpackTableSync _decoderTableSync = new(encoderMaxCapacity: 4096, decoderMaxCapacity: 4096);
    private readonly Http3ServerDecoder _decoder;

    public ServerRequestDecoderSpec()
    {
        _decoder = new Http3ServerDecoder(_decoderTableSync);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void DecodeHeaders_GET_with_all_pseudoheaders_returns_correct_method_and_uri()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/index.html"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        var headerBlock = _encoderTableSync.Encoder.Encode(headers);

        // Synchronize encoder instructions to decoder
        var encoderInstructions = _encoderTableSync.Encoder.EncoderInstructions;
        if (!encoderInstructions.IsEmpty)
        {
            _decoderTableSync.ProcessEncoderInstructions(encoderInstructions.Span);
        }

        var frame = new HeadersFrame(headerBlock);
        var state = new StreamState();
        state.Initialize(streamId: 1);

        var feature = _decoder.DecodeHeadersToFeature(frame, state, endStream: true);

        Assert.NotNull(feature);
        Assert.Equal("GET", feature.Method);
        Assert.Equal("/index.html", feature.RawTarget);
        Assert.Equal("https", feature.Scheme);
        Assert.Equal("HTTP/3", feature.Protocol);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void DecodeHeaders_POST_with_content_type_includes_content_headers()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "POST"),
            (":path", "/api/data"),
            (":scheme", "https"),
            (":authority", "api.example.com"),
            ("content-type", "application/json"),
            ("content-length", "42"),
        };

        var headerBlock = _encoderTableSync.Encoder.Encode(headers);

        // Synchronize encoder instructions to decoder
        var encoderInstructions = _encoderTableSync.Encoder.EncoderInstructions;
        if (!encoderInstructions.IsEmpty)
        {
            _decoderTableSync.ProcessEncoderInstructions(encoderInstructions.Span);
        }

        var frame = new HeadersFrame(headerBlock);
        var state = new StreamState();
        state.Initialize(streamId: 1);

        var feature = _decoder.DecodeHeadersToFeature(frame, state, endStream: true);

        Assert.NotNull(feature);
        Assert.Equal("POST", feature.Method);
        Assert.True(state.HasContentHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void DecodeHeaders_missing_method_throws_HttpProtocolException()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":path", "/index.html"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        var headerBlock = _encoderTableSync.Encoder.Encode(headers);

        // Synchronize encoder instructions to decoder
        var encoderInstructions = _encoderTableSync.Encoder.EncoderInstructions;
        if (!encoderInstructions.IsEmpty)
        {
            _decoderTableSync.ProcessEncoderInstructions(encoderInstructions.Span);
        }

        var frame = new HeadersFrame(headerBlock);
        var state = new StreamState();
        state.Initialize(streamId: 1);

        var ex = Assert.Throws<HttpProtocolException>(() => { _decoder.DecodeHeadersToFeature(frame, state, endStream: true); });

        Assert.Contains(":method", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void DecodeHeaders_missing_path_for_non_CONNECT_throws_HttpProtocolException()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        var headerBlock = _encoderTableSync.Encoder.Encode(headers);

        // Synchronize encoder instructions to decoder
        var encoderInstructions = _encoderTableSync.Encoder.EncoderInstructions;
        if (!encoderInstructions.IsEmpty)
        {
            _decoderTableSync.ProcessEncoderInstructions(encoderInstructions.Span);
        }

        var frame = new HeadersFrame(headerBlock);
        var state = new StreamState();
        state.Initialize(streamId: 1);

        var ex = Assert.Throws<HttpProtocolException>(() => { _decoder.DecodeHeadersToFeature(frame, state, endStream: true); });

        Assert.Contains(":path", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void DecodeHeaders_CONNECT_without_path_and_scheme_succeeds()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "CONNECT"),
            (":authority", "example.com:443"),
        };

        var headerBlock = _encoderTableSync.Encoder.Encode(headers);

        // Synchronize encoder instructions to decoder
        var encoderInstructions = _encoderTableSync.Encoder.EncoderInstructions;
        if (!encoderInstructions.IsEmpty)
        {
            _decoderTableSync.ProcessEncoderInstructions(encoderInstructions.Span);
        }

        var frame = new HeadersFrame(headerBlock);
        var state = new StreamState();
        state.Initialize(streamId: 1);

        var feature = _decoder.DecodeHeadersToFeature(frame, state, endStream: true);

        Assert.NotNull(feature);
        Assert.Equal("CONNECT", feature.Method);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void DecodeHeaders_with_regular_headers_includes_them_in_request()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            ("user-agent", "test-client/1.0"),
            ("accept", "application/json"),
        };

        var headerBlock = _encoderTableSync.Encoder.Encode(headers);

        // Synchronize encoder instructions to decoder
        var encoderInstructions = _encoderTableSync.Encoder.EncoderInstructions;
        if (!encoderInstructions.IsEmpty)
        {
            _decoderTableSync.ProcessEncoderInstructions(encoderInstructions.Span);
        }

        var frame = new HeadersFrame(headerBlock);
        var state = new StreamState();
        state.Initialize(streamId: 1);

        var feature = _decoder.DecodeHeadersToFeature(frame, state, endStream: true);

        Assert.NotNull(feature);
        Assert.True(feature.Headers.ContainsKey("user-agent"));
        Assert.True(feature.Headers.ContainsKey("accept"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void DecodeHeaders_missing_authority_for_non_CONNECT_throws_HttpProtocolException()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
        };

        var headerBlock = _encoderTableSync.Encoder.Encode(headers);

        // Synchronize encoder instructions to decoder
        var encoderInstructions = _encoderTableSync.Encoder.EncoderInstructions;
        if (!encoderInstructions.IsEmpty)
        {
            _decoderTableSync.ProcessEncoderInstructions(encoderInstructions.Span);
        }

        var frame = new HeadersFrame(headerBlock);
        var state = new StreamState();
        state.Initialize(streamId: 1);

        var ex = Assert.Throws<HttpProtocolException>(() => { _decoder.DecodeHeadersToFeature(frame, state, endStream: true); });

        Assert.Contains(":authority", ex.Message);
    }
}