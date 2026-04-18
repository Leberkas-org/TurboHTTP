using TurboHTTP.Protocol.Http3;
using TurboHTTP.Protocol.Http3.Qpack;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.Shared;

public sealed class H3ResponseBuilderSpec
{
    [Fact(Timeout = 5000)]
    public void Build_should_produce_valid_settings_headers_data_sequence()
    {
        var bytes = new H3ResponseBuilder()
            .Settings(
                (Http3SettingsIdentifier.QpackMaxTableCapacity, 0),
                (Http3SettingsIdentifier.MaxFieldSectionSize, 8192))
            .Headers(200, [("content-type", "text/plain")])
            .Data("hello")
            .Build();

        using var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(bytes.AsSpan(), out var bytesConsumed);

        Assert.Equal(bytes.Length, bytesConsumed);
        Assert.Equal(3, frames.Count);

        var settings = Assert.IsType<Http3SettingsFrame>(frames[0]);
        Assert.Equal(2, settings.Parameters.Count);
        Assert.Equal(Http3SettingsIdentifier.QpackMaxTableCapacity, settings.Parameters[0].Identifier);
        Assert.Equal(0L, settings.Parameters[0].Value);
        Assert.Equal(Http3SettingsIdentifier.MaxFieldSectionSize, settings.Parameters[1].Identifier);
        Assert.Equal(8192L, settings.Parameters[1].Value);

        var headers = Assert.IsType<Http3HeadersFrame>(frames[1]);
        var qpackDecoder = new QpackDecoder();
        var decoded = qpackDecoder.Decode(headers.HeaderBlock.Span);
        Assert.Contains(decoded, h => h.Name == ":status" && h.Value == "200");
        Assert.Contains(decoded, h => h.Name == "content-type" && h.Value == "text/plain");

        var data = Assert.IsType<Http3DataFrame>(frames[2]);
        Assert.Equal("hello"u8.ToArray(), data.Data.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void Build_should_produce_valid_empty_settings()
    {
        var bytes = new H3ResponseBuilder()
            .Settings()
            .Build();

        using var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(bytes.AsSpan(), out var bytesConsumed);

        Assert.Equal(bytes.Length, bytesConsumed);
        Assert.Single(frames);
        var settings = Assert.IsType<Http3SettingsFrame>(frames[0]);
        Assert.Empty(settings.Parameters);
    }

    [Fact(Timeout = 5000)]
    public void Build_should_produce_valid_goaway_frame()
    {
        var bytes = new H3ResponseBuilder()
            .GoAway(4)
            .Build();

        using var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(bytes.AsSpan(), out var bytesConsumed);

        Assert.Equal(bytes.Length, bytesConsumed);
        Assert.Single(frames);
        var goaway = Assert.IsType<Http3GoAwayFrame>(frames[0]);
        Assert.Equal(4L, goaway.StreamId);
    }

    [Fact(Timeout = 5000)]
    public void Build_should_produce_headers_only_response()
    {
        var bytes = new H3ResponseBuilder()
            .Headers(204)
            .Build();

        using var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(bytes.AsSpan(), out var bytesConsumed);

        Assert.Equal(bytes.Length, bytesConsumed);
        Assert.Single(frames);
        var headers = Assert.IsType<Http3HeadersFrame>(frames[0]);

        var qpackDecoder = new QpackDecoder();
        var decoded = qpackDecoder.Decode(headers.HeaderBlock.Span);
        Assert.Single(decoded);
        Assert.Equal(":status", decoded[0].Name);
        Assert.Equal("204", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    public void Build_should_produce_valid_max_push_id_frame()
    {
        var bytes = new H3ResponseBuilder()
            .MaxPushId(7)
            .Build();

        using var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(bytes.AsSpan(), out var bytesConsumed);

        Assert.Equal(bytes.Length, bytesConsumed);
        Assert.Single(frames);
        var maxPush = Assert.IsType<Http3MaxPushIdFrame>(frames[0]);
        Assert.Equal(7L, maxPush.PushId);
    }

    [Fact(Timeout = 5000)]
    public void Build_should_produce_byte_exact_round_trip_through_decoder()
    {
        var builder = new H3ResponseBuilder();
        var bytes = builder
            .Settings((Http3SettingsIdentifier.QpackMaxTableCapacity, 0))
            .Headers(200)
            .Data("body")
            .GoAway(0)
            .Build();

        using var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(bytes.AsSpan(), out var bytesConsumed);

        Assert.Equal(bytes.Length, bytesConsumed);
        Assert.Equal(4, frames.Count);
        Assert.IsType<Http3SettingsFrame>(frames[0]);
        Assert.IsType<Http3HeadersFrame>(frames[1]);
        Assert.IsType<Http3DataFrame>(frames[2]);
        Assert.IsType<Http3GoAwayFrame>(frames[3]);
    }

    [Fact(Timeout = 5000)]
    public void Build_should_produce_valid_cancel_push_frame()
    {
        var bytes = new H3ResponseBuilder()
            .CancelPush(3)
            .Build();

        using var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(bytes.AsSpan(), out var bytesConsumed);

        Assert.Equal(bytes.Length, bytesConsumed);
        Assert.Single(frames);
        var cancel = Assert.IsType<Http3CancelPushFrame>(frames[0]);
        Assert.Equal(3L, cancel.PushId);
    }
}