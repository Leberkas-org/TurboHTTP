using System.Net;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H2;

public sealed class MaxConcurrentStreamsSpec : AcceptanceTestBase
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public async Task Five_concurrent_requests_should_complete_with_limiter_active()
    {
        const int count = 5;
        var enc = new HpackEncoder(useHuffman: false);
        var settings = new SettingsFrame([]).Serialize();

        var frameBuffers = new List<byte[]> { settings };
        for (var i = 0; i < count; i++)
        {
            var streamId = 1 + i * 2;
            var hf = new HeadersFrame(streamId,
                enc.Encode([(":status", "200"), ("content-length", "7")]),
                endStream: false, endHeaders: true).Serialize();
            var df = new DataFrame(streamId, "delayed"u8.ToArray(), endStream: true).Serialize();
            var combined = new byte[hf.Length + df.Length];
            hf.CopyTo(combined, 0);
            df.CopyTo(combined, hf.Length);
            frameBuffers.Add(combined);
        }

        var requests = Enumerable.Range(0, count)
            .Select(_ => new HttpRequestMessage(HttpMethod.Get, "http://localhost/delay/200")
            {
                Version = HttpVersion.Version20
            })
            .ToList();

        var (responses, _) = await SendH2EngineAsyncMany(
            CreateHttp20Engine().CreateFlow(), requests, count, frameBuffers.ToArray());

        Assert.Equal(count, responses.Count);
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public async Task Sequential_requests_should_succeed_through_limiter()
    {
        for (var i = 0; i < 5; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
            {
                Version = HttpVersion.Version20
            };

            var serverFrames = new H2ResponseBuilder()
                .Settings()
                .SettingsAck()
                .Headers(1, 200, [("content-length", "11")], endStream: false)
                .Data(1, "Hello World")
                .Build();

            var (response, _) = await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, serverFrames);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Equal("Hello World", body);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public async Task Ten_concurrent_requests_should_complete_successfully()
    {
        const int count = 10;
        var enc = new HpackEncoder(useHuffman: false);
        var settings = new SettingsFrame([]).Serialize();

        var frameBuffers = new List<byte[]> { settings };
        for (var i = 0; i < count; i++)
        {
            var streamId = 1 + i * 2;
            var hf = new HeadersFrame(streamId,
                enc.Encode([(":status", "200"), ("content-length", "4")]),
                endStream: false, endHeaders: true).Serialize();
            var df = new DataFrame(streamId, "pong"u8.ToArray(), endStream: true).Serialize();
            var combined = new byte[hf.Length + df.Length];
            hf.CopyTo(combined, 0);
            df.CopyTo(combined, hf.Length);
            frameBuffers.Add(combined);
        }

        var requests = Enumerable.Range(0, count)
            .Select(_ => new HttpRequestMessage(HttpMethod.Get, "http://localhost/ping")
            {
                Version = HttpVersion.Version20
            })
            .ToList();

        var (responses, _) = await SendH2EngineAsyncMany(
            CreateHttp20Engine().CreateFlow(), requests, count, frameBuffers.ToArray());

        Assert.Equal(count, responses.Count);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public async Task Concurrent_delayed_requests_should_complete_as_streams_free_up()
    {
        const int count = 5;
        var enc = new HpackEncoder(useHuffman: false);
        var settings = new SettingsFrame([]).Serialize();

        var frameBuffers = new List<byte[]> { settings };
        for (var i = 0; i < count; i++)
        {
            var streamId = 1 + i * 2;
            var hf = new HeadersFrame(streamId,
                enc.Encode([(":status", "200"), ("content-length", "7")]),
                endStream: false, endHeaders: true).Serialize();
            var df = new DataFrame(streamId, "delayed"u8.ToArray(), endStream: true).Serialize();
            var combined = new byte[hf.Length + df.Length];
            hf.CopyTo(combined, 0);
            df.CopyTo(combined, hf.Length);
            frameBuffers.Add(combined);
        }

        var requests = Enumerable.Range(0, count)
            .Select(_ => new HttpRequestMessage(HttpMethod.Get, "http://localhost/delay/300")
            {
                Version = HttpVersion.Version20
            })
            .ToList();

        var (responses, _) = await SendH2EngineAsyncMany(
            CreateHttp20Engine().CreateFlow(), requests, count, frameBuffers.ToArray());

        Assert.Equal(count, responses.Count);
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Equal("delayed", body);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public async Task Mixed_concurrent_requests_should_complete()
    {
        const int count = 5;
        var enc = new HpackEncoder(useHuffman: false);
        var settings = new SettingsFrame([]).Serialize();

        var frameBuffers = new List<byte[]> { settings };
        for (var i = 0; i < count; i++)
        {
            var streamId = 1 + i * 2;
            var hf = new HeadersFrame(streamId,
                enc.Encode([(":status", "200"), ("content-length", "2")]),
                endStream: false, endHeaders: true).Serialize();
            var df = new DataFrame(streamId, "ok"u8.ToArray(), endStream: true).Serialize();
            var combined = new byte[hf.Length + df.Length];
            hf.CopyTo(combined, 0);
            df.CopyTo(combined, hf.Length);
            frameBuffers.Add(combined);
        }

        var endpoints = new[] { "/hello", "/ping", "/delay/100", "/hello", "/ping" };
        var requests = endpoints
            .Select(path => new HttpRequestMessage(HttpMethod.Get, $"http://localhost{path}")
            {
                Version = HttpVersion.Version20
            })
            .ToList();

        var (responses, _) = await SendH2EngineAsyncMany(
            CreateHttp20Engine().CreateFlow(), requests, count, frameBuffers.ToArray());

        Assert.Equal(count, responses.Count);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }
}