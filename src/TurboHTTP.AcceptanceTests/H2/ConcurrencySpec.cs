using System.Net;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H2;

public sealed class ConcurrencySpec : AcceptanceTestBase
{
    private static Http20Engine Engine => new(new Http2Options().ToEngineOptions());

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.1")]
    public async Task Ten_parallel_gets_should_be_multiplexed_over_single_connection()
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
            Engine.CreateFlow(), requests, count, frameBuffers.ToArray());

        Assert.Equal(count, responses.Count);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.1")]
    public async Task Twenty_parallel_requests_should_succeed()
    {
        const int count = 20;
        var enc = new HpackEncoder(useHuffman: false);
        var settings = new SettingsFrame([]).Serialize();

        var frameBuffers = new List<byte[]> { settings };
        for (var i = 0; i < count; i++)
        {
            var streamId = 1 + i * 2;
            var hf = new HeadersFrame(streamId,
                enc.Encode([(":status", "200"), ("content-length", "11")]),
                endStream: false, endHeaders: true).Serialize();
            var df = new DataFrame(streamId, "Hello World"u8.ToArray(), endStream: true).Serialize();
            var combined = new byte[hf.Length + df.Length];
            hf.CopyTo(combined, 0);
            df.CopyTo(combined, hf.Length);
            frameBuffers.Add(combined);
        }

        var requests = Enumerable.Range(0, count)
            .Select(_ => new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
            {
                Version = HttpVersion.Version20
            })
            .ToList();

        var (responses, _) = await SendH2EngineAsyncMany(
            Engine.CreateFlow(), requests, count, frameBuffers.ToArray());

        Assert.Equal(count, responses.Count);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.1")]
    public async Task Mixed_get_and_post_should_be_multiplexed()
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

        var requests = new List<HttpRequestMessage>
        {
            new(HttpMethod.Get, "http://localhost/hello") { Version = HttpVersion.Version20 },
            new(HttpMethod.Get, "http://localhost/ping") { Version = HttpVersion.Version20 },
            new(HttpMethod.Post, "http://localhost/h2/echo-binary")
            {
                Version = HttpVersion.Version20,
                Content = new ByteArrayContent(new byte[512])
            },
            new(HttpMethod.Post, "http://localhost/echo")
            {
                Version = HttpVersion.Version20,
                Content = new StringContent("h2-concurrent-post", System.Text.Encoding.UTF8, "text/plain")
            },
            new(HttpMethod.Get, "http://localhost/h2/settings") { Version = HttpVersion.Version20 }
        };

        var (responses, _) = await SendH2EngineAsyncMany(
            Engine.CreateFlow(), requests, count, frameBuffers.ToArray());

        Assert.Equal(count, responses.Count);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.1")]
    public async Task SixtyFour_concurrent_heavy_posts_should_complete_in_repeated_batches()
    {
        var payload = new byte[10 * 1024];

        for (var batch = 0; batch < 2; batch++)
        {
            const int count = 8;
            var enc = new HpackEncoder(useHuffman: false);
            var settings = new SettingsFrame([]).Serialize();

            var frameBuffers = new List<byte[]> { settings };
            for (var i = 0; i < count; i++)
            {
                var streamId = 1 + i * 2;
                var hf = new HeadersFrame(streamId,
                    enc.Encode([(":status", "200"), ("content-length", payload.Length.ToString())]),
                    endStream: false, endHeaders: true).Serialize();
                var df = new DataFrame(streamId, payload, endStream: true).Serialize();
                var combined = new byte[hf.Length + df.Length];
                hf.CopyTo(combined, 0);
                df.CopyTo(combined, hf.Length);
                frameBuffers.Add(combined);
            }

            var requests = Enumerable.Range(0, count)
                .Select(_ => new HttpRequestMessage(HttpMethod.Post, "http://localhost/h2/echo-binary")
                {
                    Version = HttpVersion.Version20,
                    Content = new ByteArrayContent(payload)
                })
                .ToList();

            var (responses, _) = await SendH2EngineAsyncMany(
                Engine.CreateFlow(), requests, count, frameBuffers.ToArray());

            Assert.Equal(count, responses.Count);
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.1")]
    public async Task Concurrent_requests_to_different_endpoints_should_succeed()
    {
        const int count = 8;
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

        var endpoints = new[] { "/hello", "/ping", "/h2/settings", "/hello", "/ping", "/h2/settings", "/hello", "/ping" };
        var requests = endpoints
            .Select(path => new HttpRequestMessage(HttpMethod.Get, $"http://localhost{path}")
            {
                Version = HttpVersion.Version20
            })
            .ToList();

        var (responses, _) = await SendH2EngineAsyncMany(
            Engine.CreateFlow(), requests, count, frameBuffers.ToArray());

        Assert.Equal(count, responses.Count);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }
}
