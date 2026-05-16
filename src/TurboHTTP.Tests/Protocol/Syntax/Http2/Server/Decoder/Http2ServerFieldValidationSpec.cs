using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server.Decoder;

public sealed class Http2ServerFieldValidationSpec
{
    private readonly HpackEncoder _encoder = new(useHuffman: false);
    private readonly Http2ServerDecoder _decoder = new();

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void DecodeHeaders_regular_headers_included_in_request()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
            new(":path", "/"),
            new(":scheme", "https"),
            new(":authority", "example.com"),
            new("user-agent", "test-client/1.0"),
            new("accept", "application/json"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var request = _decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(request);
        Assert.True(request.Headers.Contains("user-agent"));
        Assert.True(request.Headers.Contains("accept"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void DecodeHeaders_custom_headers_preserved()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
            new(":path", "/api/data"),
            new(":scheme", "https"),
            new(":authority", "example.com"),
            new("x-custom-header", "custom-value"),
            new("x-trace-id", "abc123"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var request = _decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(request);
        Assert.True(request.Headers.Contains("x-custom-header"));
        Assert.Equal("custom-value", request.Headers.GetValues("x-custom-header").FirstOrDefault());
        Assert.True(request.Headers.Contains("x-trace-id"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void DecodeHeaders_content_type_handled_as_content_header()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "POST"),
            new(":path", "/api/data"),
            new(":scheme", "https"),
            new(":authority", "example.com"),
            new("content-type", "application/json"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var request = _decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(request);
        Assert.NotNull(request.Content);
        Assert.True(request.Content.Headers.Contains("content-type"));
        Assert.Equal("application/json", request.Content.Headers.ContentType?.MediaType);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeHeaders_endStream_false_returns_null_waiting_for_body()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "POST"),
            new(":path", "/data"),
            new(":scheme", "https"),
            new(":authority", "example.com"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var request = _decoder.DecodeHeaders(streamId: 1, endStream: false, state);

        Assert.Null(request);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void DecodeHeaders_content_length_handled_as_content_header()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "POST"),
            new(":path", "/api/data"),
            new(":scheme", "https"),
            new(":authority", "example.com"),
            new("content-length", "42"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var request = _decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(request);
        Assert.NotNull(request.Content);
        Assert.True(request.Content.Headers.Contains("content-length"));
    }

    private byte[] EncodeHeaders(List<HpackHeader> headers)
    {
        using var owner = System.Buffers.MemoryPool<byte>.Shared.Rent(4096);
        var span = owner.Memory.Span;
        var bytesWritten = _encoder.Encode(headers, ref span, useHuffman: false);
        return owner.Memory[..bytesWritten].ToArray();
    }

    private static StreamState BuildStreamState(byte[] headerBlock)
    {
        var state = new StreamState();
        state.AppendHeader(headerBlock);
        return state;
    }
}