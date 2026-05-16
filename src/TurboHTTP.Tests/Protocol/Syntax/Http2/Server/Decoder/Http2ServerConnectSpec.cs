using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server.Decoder;

public sealed class Http2ServerConnectSpec
{
    private readonly HpackEncoder _encoder = new(useHuffman: false);
    private readonly Http2ServerDecoder _decoder = new();

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.5")]
    public void DecodeHeaders_CONNECT_without_path_and_scheme_succeeds()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "CONNECT"),
            new(":authority", "example.com:443"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var request = _decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(request);
        Assert.Equal("CONNECT", request.Method.Method);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.5")]
    public void DecodeHeaders_CONNECT_sets_authority_from_pseudo_header()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "CONNECT"),
            new(":authority", "secure.example.com:8443"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var request = _decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(request);
        Assert.Equal("CONNECT", request.Method.Method);
        // RequestUri should not be set for CONNECT requests
        Assert.Null(request.RequestUri);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.5")]
    public void DecodeHeaders_CONNECT_with_endStream_false_returns_null_tunnel_continues()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "CONNECT"),
            new(":authority", "example.com:443"),
        };

        var encoded = EncodeHeaders(headers);
        var state = BuildStreamState(encoded);

        var request = _decoder.DecodeHeaders(streamId: 1, endStream: false, state);

        // With endStream=false, request is not yet complete (waiting for body/tunnel data)
        Assert.Null(request);
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