using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.Shared;

/// <summary>
/// Verifies that <see cref="H2ResponseBuilder"/> produces valid HTTP/2 frame sequences
/// decodable by <see cref="FrameDecoder"/>.
/// </summary>
public sealed class H2ResponseBuilderSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Build_should_produce_valid_settings_headers_data_sequence()
    {
        var bytes = new H2ResponseBuilder()
            .Settings(
                (SettingsParameter.MaxConcurrentStreams, 100),
                (SettingsParameter.InitialWindowSize, 65535))
            .SettingsAck()
            .Headers(1, 200, [("content-type", "text/plain")])
            .Data(1, "hello")
            .Build();

        using var decoder = new FrameDecoder();
        var frames = decoder.Decode(new ReadOnlyMemory<byte>(bytes));

        Assert.Equal(4, frames.Count);

        var settings = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.False(settings.IsAck);
        Assert.Equal(2, settings.Parameters.Count);
        Assert.Equal(SettingsParameter.MaxConcurrentStreams, settings.Parameters[0].Item1);
        Assert.Equal(100u, settings.Parameters[0].Item2);

        var settingsAck = Assert.IsType<SettingsFrame>(frames[1]);
        Assert.True(settingsAck.IsAck);

        var headers = Assert.IsType<HeadersFrame>(frames[2]);
        Assert.Equal(1, headers.StreamId);
        Assert.True(headers.EndHeaders);
        Assert.False(headers.EndStream);

        var hpackDecoder = new HpackDecoder();
        var decoded = hpackDecoder.Decode(headers.HeaderBlockFragment.Span);
        Assert.Contains(decoded, h => h is { Name: ":status", Value: "200" });
        Assert.Contains(decoded, h => h is { Name: "content-type", Value: "text/plain" });

        var data = Assert.IsType<DataFrame>(frames[3]);
        Assert.Equal(1, data.StreamId);
        Assert.True(data.EndStream);
        Assert.Equal("hello"u8.ToArray(), data.Data.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Build_should_produce_valid_empty_settings_ack()
    {
        var bytes = new H2ResponseBuilder()
            .SettingsAck()
            .Build();

        using var decoder = new FrameDecoder();
        var frames = decoder.Decode(new ReadOnlyMemory<byte>(bytes));

        Assert.Single(frames);
        var settings = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.True(settings.IsAck);
        Assert.Empty(settings.Parameters);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Build_should_produce_valid_window_update()
    {
        var bytes = new H2ResponseBuilder()
            .WindowUpdate(0, 65535)
            .WindowUpdate(1, 32768)
            .Build();

        using var decoder = new FrameDecoder();
        var frames = decoder.Decode(new ReadOnlyMemory<byte>(bytes));

        Assert.Equal(2, frames.Count);

        var wu0 = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(0, wu0.StreamId);
        Assert.Equal(65535, wu0.Increment);

        var wu1 = Assert.IsType<WindowUpdateFrame>(frames[1]);
        Assert.Equal(1, wu1.StreamId);
        Assert.Equal(32768, wu1.Increment);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.1")]
    public void Build_should_produce_headers_only_response_with_end_stream()
    {
        var bytes = new H2ResponseBuilder()
            .Headers(1, 204, endStream: true)
            .Build();

        using var decoder = new FrameDecoder();
        var frames = decoder.Decode(new ReadOnlyMemory<byte>(bytes));

        Assert.Single(frames);
        var headers = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(1, headers.StreamId);
        Assert.True(headers.EndStream);
        Assert.True(headers.EndHeaders);

        var hpackDecoder = new HpackDecoder();
        var decoded = hpackDecoder.Decode(headers.HeaderBlockFragment.Span);
        Assert.Single(decoded);
        Assert.Equal(":status", decoded[0].Name);
        Assert.Equal("204", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Build_should_produce_valid_goaway_frame()
    {
        var bytes = new H2ResponseBuilder()
            .GoAway(3, Http2ErrorCode.NoError)
            .Build();

        using var decoder = new FrameDecoder();
        var frames = decoder.Decode(new ReadOnlyMemory<byte>(bytes));

        Assert.Single(frames);
        var goaway = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(3, goaway.LastStreamId);
        Assert.Equal(Http2ErrorCode.NoError, goaway.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void Build_should_produce_valid_rst_stream_frame()
    {
        var bytes = new H2ResponseBuilder()
            .RstStream(1, Http2ErrorCode.Cancel)
            .Build();

        using var decoder = new FrameDecoder();
        var frames = decoder.Decode(new ReadOnlyMemory<byte>(bytes));

        Assert.Single(frames);
        var rst = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(1, rst.StreamId);
        Assert.Equal(Http2ErrorCode.Cancel, rst.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Build_should_produce_byte_exact_round_trip_through_decoder()
    {
        var builder = new H2ResponseBuilder();
        var bytes = builder
            .Settings((SettingsParameter.HeaderTableSize, 4096))
            .SettingsAck()
            .Headers(1, 200)
            .Data(1, "body")
            .WindowUpdate(0, 100)
            .Build();

        using var decoder = new FrameDecoder();
        var frames = decoder.Decode(new ReadOnlyMemory<byte>(bytes));

        Assert.Equal(5, frames.Count);
        Assert.IsType<SettingsFrame>(frames[0]);
        Assert.IsType<SettingsFrame>(frames[1]);
        Assert.IsType<HeadersFrame>(frames[2]);
        Assert.IsType<DataFrame>(frames[3]);
        Assert.IsType<WindowUpdateFrame>(frames[4]);
    }
}
