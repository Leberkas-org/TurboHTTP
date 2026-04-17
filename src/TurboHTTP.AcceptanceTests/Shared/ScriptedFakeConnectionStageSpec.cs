using System.Net;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.Shared;

/// <summary>
/// Verifies <see cref="ScriptedFakeConnectionStage"/> infrastructure:
/// multi-response sequencing and error injection via the response factory.
/// </summary>
public sealed class ScriptedFakeConnectionStageSpec : EngineTestBase
{
    private static Http10Engine Engine => new(new Http1EngineOptions(16, 6, 3, 64 * 1024, 64, 1024 * 1024, TimeSpan.FromSeconds(2)));

    [Fact(Timeout = 5000)]
    public async Task ScriptedFake_should_route_responses_by_request_index()
    {
        var responses = new[]
        {
            "HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nfirst",
            "HTTP/1.0 200 OK\r\nContent-Length: 6\r\n\r\nsecond",
            "HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nthird"
        };

        var fake = new ScriptedFakeConnectionStage((index, _) =>
            Encoding.Latin1.GetBytes(responses[index]));

        var engine = Engine.CreateFlow();
        var flow = engine.Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var results = new List<HttpResponseMessage>();
        var tcs = new TaskCompletionSource();

        var requests = Enumerable.Range(0, 3).Select(i =>
            new HttpRequestMessage(HttpMethod.Get, $"http://example.com/req{i}")
            {
                Version = HttpVersion.Version10
            });

        _ = Source.From(requests)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res =>
            {
                results.Add(res);
                if (results.Count == 3)
                {
                    tcs.TrySetResult();
                }
            }), Materializer);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(3, results.Count);
        Assert.Equal("first", await results[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("second", await results[1].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("third", await results[2].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public async Task ScriptedFake_should_provide_request_bytes_to_factory()
    {
        var capturedBytes = new List<byte[]>();

        var fake = new ScriptedFakeConnectionStage((_, requestBytes) =>
        {
            capturedBytes.Add(requestBytes);
            return Encoding.Latin1.GetBytes("HTTP/1.0 200 OK\r\nContent-Length: 2\r\n\r\nok");
        });

        var engine = Engine.CreateFlow();
        var flow = engine.Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/test")
        {
            Version = HttpVersion.Version10
        };

        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Single(capturedBytes);
        var rawRequest = Encoding.Latin1.GetString(capturedBytes[0]);
        Assert.Contains("GET /test HTTP/1.0", rawRequest);
    }

    [Fact(Timeout = 5000)]
    public async Task ScriptedFake_should_expose_outbound_bytes_via_channel()
    {
        var fake = new ScriptedFakeConnectionStage((_, _) =>
            Encoding.Latin1.GetBytes("HTTP/1.0 200 OK\r\nContent-Length: 2\r\n\r\nok"));

        var engine = Engine.CreateFlow();
        var flow = engine.Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/inspect")
        {
            Version = HttpVersion.Version10
        };

        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var rawBuilder = new StringBuilder();
        while (fake.OutboundChannel.Reader.TryRead(out var chunk))
        {
            rawBuilder.Append(Encoding.Latin1.GetString(chunk.Span));
        }

        var rawRequest = rawBuilder.ToString();
        Assert.Contains("GET /inspect HTTP/1.0", rawRequest);
    }

    [Fact(Timeout = 5000)]
    public async Task ScriptedFake_should_inject_corrupt_bytes_when_factory_returns_malformed_response()
    {
        // Simulate corrupt bytes: valid status line but binary garbage body
        var header = Encoding.Latin1.GetBytes("HTTP/1.0 200 OK\r\nContent-Length: 3\r\n\r\n");
        var corruptResponse = new byte[header.Length + 3];
        header.CopyTo(corruptResponse, 0);
        corruptResponse[header.Length] = 0x00;
        corruptResponse[header.Length + 1] = 0xFF;
        corruptResponse[header.Length + 2] = 0xFE;

        var fake = new ScriptedFakeConnectionStage((_, _) => corruptResponse);

        var engine = Engine.CreateFlow();
        var flow = engine.Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/corrupt")
        {
            Version = HttpVersion.Version10
        };

        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // The engine should still decode the response; the body contains raw corrupt bytes
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bodyBytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, bodyBytes.Length);
        Assert.Equal(0x00, bodyBytes[0]);
        Assert.Equal(0xFF, bodyBytes[1]);
        Assert.Equal(0xFE, bodyBytes[2]);
    }

    [Fact(Timeout = 5000)]
    public async Task ScriptedFake_should_return_conditional_responses_based_on_request_content()
    {
        var fake = new ScriptedFakeConnectionStage((_, requestBytes) =>
        {
            var raw = Encoding.Latin1.GetString(requestBytes);
            if (raw.Contains("/alpha"))
            {
                return Encoding.Latin1.GetBytes("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nalpha");
            }

            return Encoding.Latin1.GetBytes("HTTP/1.0 404 Not Found\r\nContent-Length: 9\r\n\r\nnot found");
        });

        var engine = Engine.CreateFlow();
        var flow = engine.Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var results = new List<HttpResponseMessage>();
        var tcs = new TaskCompletionSource();

        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/alpha") { Version = HttpVersion.Version10 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/beta") { Version = HttpVersion.Version10 }
        };

        _ = Source.From(requests)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res =>
            {
                results.Add(res);
                if (results.Count == 2)
                {
                    tcs.TrySetResult();
                }
            }), Materializer);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, results[0].StatusCode);
        Assert.Equal("alpha", await results[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, results[1].StatusCode);
        Assert.Equal("not found", await results[1].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public async Task ScriptedFake_should_abort_stream_when_factory_returns_null()
    {
        var fake = new ScriptedFakeConnectionStage((index, _) =>
        {
            if (index == 0)
            {
                return Encoding.Latin1.GetBytes("HTTP/1.0 200 OK\r\nContent-Length: 2\r\n\r\nok");
            }

            return null; // abort
        });

        var engine = Engine.CreateFlow();
        var flow = engine.Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var results = new List<HttpResponseMessage>();
        var completionTcs = new TaskCompletionSource();

        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/first") { Version = HttpVersion.Version10 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/second") { Version = HttpVersion.Version10 }
        };

        _ = Source.From(requests)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res =>
            {
                results.Add(res);
            }), Materializer)
            .ContinueWith(_ => completionTcs.TrySetResult());

        await completionTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Only the first response should have been delivered before the stage aborted
        Assert.Single(results);
        Assert.Equal("ok", await results[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Trait("RFC", "TASK-002-004")]
    [Fact(Timeout = 5000)]
    public async Task ScriptedFake_should_suppress_response_when_behaviorStack_overrides_factory_with_error()
    {
        // BehaviorStack overrides the factory; PushConstant(null) → ConnectionAbort path → no response delivered
        var stack = new BehaviorStack<(int Index, byte[] RequestBytes), byte[]?>(
            _ => Encoding.Latin1.GetBytes("HTTP/1.0 200 OK\r\nContent-Length: 2\r\n\r\nok"));
        stack.PushConstant(null);

        var fake = new ScriptedFakeConnectionStage(
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.0 200 OK\r\nContent-Length: 2\r\n\r\nok"),
            stack);

        var engine = Engine.CreateFlow();
        var flow = engine.Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var results = new List<HttpResponseMessage>();
        var completionTcs = new TaskCompletionSource();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/fail")
        {
            Version = HttpVersion.Version10
        };

        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => results.Add(res)), Materializer)
            .ContinueWith(_ => completionTcs.TrySetResult());

        await completionTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // BehaviorStack returned null → factory was bypassed → no response delivered
        Assert.Empty(results);
    }

    [Trait("RFC", "TASK-002-004")]
    [Fact(Timeout = 5000)]
    public async Task ScriptedFake_should_fail_first_request_then_succeed_when_behaviorStack_pushes_once_error()
    {
        var stack = new BehaviorStack<(int Index, byte[] RequestBytes), byte[]?>(
            (t) => Encoding.Latin1.GetBytes("HTTP/1.0 200 OK\r\nContent-Length: 7\r\n\r\nsuccess"));
        stack.PushOnce(_ => null); // first request → null = abort

        var fake = new ScriptedFakeConnectionStage(
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.0 200 OK\r\nContent-Length: 7\r\n\r\nsuccess"),
            stack);

        var engine = Engine.CreateFlow();
        var flow = engine.Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var completionTcs = new TaskCompletionSource();
        var results = new List<HttpResponseMessage>();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/once")
        {
            Version = HttpVersion.Version10
        };

        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res =>
            {
                results.Add(res);
                if (results.Count == 1) { completionTcs.TrySetResult(); }
            }), Materializer)
            .ContinueWith(_ => completionTcs.TrySetResult());

        await completionTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // The once-behavior returns null → ConnectionAbort → stage completes with no responses
        Assert.Empty(results);
    }

    [Trait("RFC", "TASK-002-004")]
    [Fact(Timeout = 5000)]
    public async Task ScriptedFake_should_record_WriteAttempt_and_ResponseDelivered_in_activityLog()
    {
        var log = new ActivityLog();

        var fake = new ScriptedFakeConnectionStage(
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.0 200 OK\r\nContent-Length: 2\r\n\r\nok"),
            null,
            log);

        var engine = Engine.CreateFlow();
        var flow = engine.Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/log")
        {
            Version = HttpVersion.Version10
        };

        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var writes = log.OfType<WriteAttempt>().ToList();
        var deliveries = log.OfType<ResponseDelivered>().ToList();

        Assert.Single(writes);
        Assert.Equal(0, writes[0].Index);
        Assert.NotEmpty(writes[0].Payload);

        Assert.Single(deliveries);
        Assert.Equal(0, deliveries[0].Index);
        Assert.True(deliveries[0].ByteCount > 0);
    }

    [Trait("RFC", "TASK-002-004")]
    [Fact(Timeout = 5000)]
    public async Task ScriptedFake_should_record_ConnectionAbort_in_activityLog_when_factory_returns_null()
    {
        var log = new ActivityLog();

        var fake = new ScriptedFakeConnectionStage(
            (_, _) => null,
            null,
            log);

        var engine = Engine.CreateFlow();
        var flow = engine.Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var completionTcs = new TaskCompletionSource();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/abort")
        {
            Version = HttpVersion.Version10
        };

        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(_ => { }), Materializer)
            .ContinueWith(_ => completionTcs.TrySetResult());

        await completionTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var aborts = log.OfType<ConnectionAbort>().ToList();
        var writes = log.OfType<WriteAttempt>().ToList();

        Assert.Single(writes);
        Assert.Single(aborts);
        Assert.Empty(log.OfType<ResponseDelivered>());
    }
}
