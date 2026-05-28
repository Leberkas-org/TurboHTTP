using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Client.Decoder;

public sealed class ConnectTunnelSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.5")]
    public void Encoder_should_omit_scheme_and_path_when_connect_method()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var block = encoder.Encode([
            (":method", "CONNECT"),
            (":authority", "proxy.example.com:443")
        ]);

        var decoder = new HpackDecoder();
        var headers = decoder.Decode(block.Span);

        Assert.DoesNotContain(headers, h => h.Name == ":scheme");
        Assert.DoesNotContain(headers, h => h.Name == ":path");
        Assert.Contains(headers, h => h is { Name: ":method", Value: "CONNECT" });
        Assert.Contains(headers, h => h is { Name: ":authority", Value: "proxy.example.com:443" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.5")]
    public void FrameDecoder_should_decode_connect_error_from_rst_stream()
    {
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(
            new RstStreamFrame(1, Http2ErrorCode.ConnectError).Serialize());

        var rst = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(Http2ErrorCode.ConnectError, rst.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.5")]
    public void FrameDecoder_should_accept_data_frame_on_connect_stream()
    {
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(
            new DataFrame(1, "tunnel data"u8.ToArray(), endStream: false).Serialize());

        var data = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(1, data.StreamId);
        Assert.False(data.EndStream);
    }
}
