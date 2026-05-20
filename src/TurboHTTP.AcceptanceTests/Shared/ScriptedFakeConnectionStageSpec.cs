using TurboHTTP.Client;
using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.Shared;

public sealed class ScriptedFakeConnectionStageSpec : EngineTestBase
{
    private static Http10ClientEngine Engine =>
        new(new TurboClientOptions());

    [Fact(Timeout = 5000)]
    public async Task ScriptedFake_should_route_responses_by_request_index()
    {
        var responses = new[]
        {
            "HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nfirst",
            "HTTP/1.0 200 OK\r\nContent-Length: 6\r\n\r\nsecond",
            "HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nthird"
        };

        var fake = CreateScriptedConnection((index, _) =>
            Encoding.Latin1.GetBytes(responses[index]));

        var engine = Engine.CreateFlow();
        var flow = engine.Join(fake.AsFlow());

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

        var fake = CreateScriptedConnection((_, requestBytes) =>
        {
            capturedBytes.Add(requestBytes);
            return Encoding.Latin1.GetBytes("HTTP/1.0 200 OK\r\nContent-Length: 2\r\n\r\nok");
        });

        var engine = Engine.CreateFlow();
        var flow = engine.Join(fake.AsFlow());

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
        var fake = CreateScriptedConnection((_, _) =>
            Encoding.Latin1.GetBytes("HTTP/1.0 200 OK\r\nContent-Length: 2\r\n\r\nok"));

        var engine = Engine.CreateFlow();
        var flow = engine.Join(fake.AsFlow());

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
        foreach (var outbound in fake.ReceivedOutbound)
        {
            if (outbound is TransportData { Buffer: var buf })
            {
                rawBuilder.Append(Encoding.Latin1.GetString(buf.Span));
            }
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

        var fake = CreateScriptedConnection((_, _) => corruptResponse);

        var engine = Engine.CreateFlow();
        var flow = engine.Join(fake.AsFlow());

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
        var fake = CreateScriptedConnection((_, requestBytes) =>
        {
            var raw = Encoding.Latin1.GetString(requestBytes);
            if (raw.Contains("/alpha"))
            {
                return Encoding.Latin1.GetBytes("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nalpha");
            }

            return Encoding.Latin1.GetBytes("HTTP/1.0 404 Not Found\r\nContent-Length: 9\r\n\r\nnot found");
        });

        var engine = Engine.CreateFlow();
        var flow = engine.Join(fake.AsFlow());

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
        var fake = CreateScriptedConnection((index, _) =>
        {
            if (index == 0)
            {
                return Encoding.Latin1.GetBytes("HTTP/1.0 200 OK\r\nContent-Length: 2\r\n\r\nok");
            }

            return null; // abort
        });

        var engine = Engine.CreateFlow();
        var flow = engine.Join(fake.AsFlow());

        var results = new List<HttpResponseMessage>();
        var completionTcs = new TaskCompletionSource();

        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/first") { Version = HttpVersion.Version10 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/second") { Version = HttpVersion.Version10 }
        };

        _ = Source.From(requests)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => { results.Add(res); }), Materializer)
            .ContinueWith(_ => completionTcs.TrySetResult());

        await completionTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Only the first response should have been delivered before the stage aborted
        Assert.Single(results);
        Assert.Equal("ok", await results[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
