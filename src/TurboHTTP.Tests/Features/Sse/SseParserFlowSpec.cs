using System.Text;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using Servus.Akka.Sse;

namespace TurboHTTP.Tests.Features.Sse;

public sealed class SseParserFlowSpec : TestKit
{
    private readonly IMaterializer _materializer;

    public SseParserFlowSpec() : base(ActorSystem.Create("test"))
    {
        _materializer = Sys.Materializer();
    }

    private Source<ReadOnlyMemory<byte>, Akka.NotUsed> SseBytes(string raw)
    {
        return Source.Single((ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(raw));
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_parse_simple_data_event()
    {
        var result = await SseBytes("data: hello\n\n")
            .Via(SseParserFlow.Instance)
            .RunWith(Sink.Seq<ServerSentEvent>(), _materializer);

        Assert.Single(result);
        Assert.Equal("hello", result[0].Data);
        Assert.Equal("message", result[0].EventType);
        Assert.Null(result[0].Id);
        Assert.Null(result[0].Retry);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_parse_event_with_all_fields()
    {
        var raw = "event: update\ndata: payload\nid: 42\nretry: 3000\n\n";
        var result = await SseBytes(raw)
            .Via(SseParserFlow.Instance)
            .RunWith(Sink.Seq<ServerSentEvent>(), _materializer);

        Assert.Single(result);
        Assert.Equal("payload", result[0].Data);
        Assert.Equal("update", result[0].EventType);
        Assert.Equal("42", result[0].Id);
        Assert.Equal(TimeSpan.FromMilliseconds(3000), result[0].Retry);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_concatenate_multiline_data()
    {
        var raw = "data: line1\ndata: line2\ndata: line3\n\n";
        var result = await SseBytes(raw)
            .Via(SseParserFlow.Instance)
            .RunWith(Sink.Seq<ServerSentEvent>(), _materializer);

        Assert.Single(result);
        Assert.Equal("line1\nline2\nline3", result[0].Data);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_ignore_comments()
    {
        var raw = ": this is a comment\ndata: visible\n\n";
        var result = await SseBytes(raw)
            .Via(SseParserFlow.Instance)
            .RunWith(Sink.Seq<ServerSentEvent>(), _materializer);

        Assert.Single(result);
        Assert.Equal("visible", result[0].Data);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_parse_multiple_events()
    {
        var raw = "data: first\n\ndata: second\n\n";
        var result = await SseBytes(raw)
            .Via(SseParserFlow.Instance)
            .RunWith(Sink.Seq<ServerSentEvent>(), _materializer);

        Assert.Equal(2, result.Count);
        Assert.Equal("first", result[0].Data);
        Assert.Equal("second", result[1].Data);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_handle_crlf_line_endings()
    {
        var raw = "data: hello\r\n\r\n";
        var result = await SseBytes(raw)
            .Via(SseParserFlow.Instance)
            .RunWith(Sink.Seq<ServerSentEvent>(), _materializer);

        Assert.Single(result);
        Assert.Equal("hello", result[0].Data);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_handle_split_across_chunks()
    {
        var result = await Source.From(new[]
            {
                (ReadOnlyMemory<byte>)"data: hel"u8.ToArray(),
                (ReadOnlyMemory<byte>)"lo\n\n"u8.ToArray()
            })
            .Via(SseParserFlow.Instance)
            .RunWith(Sink.Seq<ServerSentEvent>(), _materializer);

        Assert.Single(result);
        Assert.Equal("hello", result[0].Data);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_strip_bom()
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var data = "data: hello\n\n"u8.ToArray();
        var combined = bom.Concat(data).ToArray();

        var result = await Source.Single((ReadOnlyMemory<byte>)combined)
            .Via(SseParserFlow.Instance)
            .RunWith(Sink.Seq<ServerSentEvent>(), _materializer);

        Assert.Single(result);
        Assert.Equal("hello", result[0].Data);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_emit_pending_event_on_completion()
    {
        var result = await SseBytes("data: final")
            .Via(SseParserFlow.Instance)
            .RunWith(Sink.Seq<ServerSentEvent>(), _materializer);

        Assert.Single(result);
        Assert.Equal("final", result[0].Data);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_skip_events_without_data()
    {
        var raw = "event: ping\n\ndata: real\n\n";
        var result = await SseBytes(raw)
            .Via(SseParserFlow.Instance)
            .RunWith(Sink.Seq<ServerSentEvent>(), _materializer);

        Assert.Single(result);
        Assert.Equal("real", result[0].Data);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_handle_field_without_value()
    {
        var raw = "data\n\n";
        var result = await SseBytes(raw)
            .Via(SseParserFlow.Instance)
            .RunWith(Sink.Seq<ServerSentEvent>(), _materializer);

        Assert.Single(result);
        Assert.Equal("", result[0].Data);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_default_event_type_to_message()
    {
        var result = await SseBytes("data: hello\n\n")
            .Via(SseParserFlow.Instance)
            .RunWith(Sink.Seq<ServerSentEvent>(), _materializer);

        Assert.Equal("message", result[0].EventType);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_handle_cr_only_line_endings()
    {
        var result = await SseBytes("data: hello\r\r")
            .Via(SseParserFlow.Instance)
            .RunWith(Sink.Seq<ServerSentEvent>(), _materializer);

        Assert.Single(result);
        Assert.Equal("hello", result[0].Data);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_reject_id_with_null()
    {
        var raw = "id: bad\0id\ndata: hello\n\n";
        var result = await SseBytes(raw)
            .Via(SseParserFlow.Instance)
            .RunWith(Sink.Seq<ServerSentEvent>(), _materializer);

        Assert.Single(result);
        Assert.Null(result[0].Id);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_ignore_retry_with_non_digits()
    {
        var raw = "retry: abc\ndata: hello\n\n";
        var result = await SseBytes(raw)
            .Via(SseParserFlow.Instance)
            .RunWith(Sink.Seq<ServerSentEvent>(), _materializer);

        Assert.Single(result);
        Assert.Null(result[0].Retry);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_ignore_unknown_fields()
    {
        var raw = "foo: bar\ndata: hello\n\n";
        var result = await SseBytes(raw)
            .Via(SseParserFlow.Instance)
            .RunWith(Sink.Seq<ServerSentEvent>(), _materializer);

        Assert.Single(result);
        Assert.Equal("hello", result[0].Data);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_not_remove_trailing_lf_from_multiline_data()
    {
        var raw = "data: a\ndata: b\n\n";
        var result = await SseBytes(raw)
            .Via(SseParserFlow.Instance)
            .RunWith(Sink.Seq<ServerSentEvent>(), _materializer);

        Assert.Single(result);
        Assert.Equal("a\nb", result[0].Data);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_parse_httpbingo_format()
    {
        var raw = "event: ping\ndata: {\"id\":0,\"timestamp\":1234}\n\nevent: ping\ndata: {\"id\":1,\"timestamp\":5678}\n\n";
        var result = await SseBytes(raw)
            .Via(SseParserFlow.Instance)
            .RunWith(Sink.Seq<ServerSentEvent>(), _materializer);

        Assert.Equal(2, result.Count);
        Assert.Equal("ping", result[0].EventType);
        Assert.Contains("\"id\":0", result[0].Data);
        Assert.Equal("ping", result[1].EventType);
        Assert.Contains("\"id\":1", result[1].Data);
    }
}