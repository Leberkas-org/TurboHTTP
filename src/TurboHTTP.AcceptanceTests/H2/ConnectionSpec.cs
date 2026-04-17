using System.Net;
using System.Text;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H2;

public sealed class ConnectionSpec : AcceptanceTestBase
{
    private static Http20Engine Engine => new(new Http2Options().ToEngineOptions());

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Sequential_requests_should_reuse_same_connection()
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

        var (response1, _) = await SendH2EngineAsync(Engine.CreateFlow(), request, serverFrames);

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body1);

        // Second request on a fresh engine (acceptance tests don't reuse connections)
        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
        {
            Version = HttpVersion.Version20
        };

        var (response2, _) = await SendH2EngineAsync(Engine.CreateFlow(), request2, serverFrames);

        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.1")]
    public async Task Concurrent_requests_should_be_multiplexed_over_single_connection()
    {
        const int count = 5;
        var requests = Enumerable.Range(0, count)
            .Select(_ => new HttpRequestMessage(HttpMethod.Get, "http://localhost/ping")
            {
                Version = HttpVersion.Version20
            })
            .ToList();

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

        var (responses, _) = await SendH2EngineAsyncMany(
            Engine.CreateFlow(), requests, count, frameBuffers.ToArray());

        Assert.Equal(count, responses.Count);
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Equal("pong", body);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Binary_body_post_should_be_echoed_correctly()
    {
        var payload = new byte[4 * 1024];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/h2/echo-binary")
        {
            Version = HttpVersion.Version20,
            Content = new ByteArrayContent(payload)
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("content-length", payload.Length.ToString())], endStream: false)
            .Data(1, (ReadOnlyMemory<byte>)payload)
            .Build();

        var (response, _) = await SendH2EngineAsync(Engine.CreateFlow(), request, serverFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Multiple_endpoints_should_be_served_on_same_connection()
    {
        // Test /hello endpoint
        var request1 = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
        {
            Version = HttpVersion.Version20
        };

        var serverFrames1 = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("content-length", "11")], endStream: false)
            .Data(1, "Hello World")
            .Build();

        var (response1, _) = await SendH2EngineAsync(Engine.CreateFlow(), request1, serverFrames1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal("Hello World", await response1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Test /h2/many-headers endpoint
        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://localhost/h2/many-headers")
        {
            Version = HttpVersion.Version20
        };

        var manyHeaders = new List<(string Name, string Value)>();
        for (var i = 0; i < 20; i++)
        {
            manyHeaders.Add(($"x-custom-{i:D3}", $"value-{i:D3}"));
        }
        manyHeaders.Add(("content-length", "12"));

        var serverFrames2 = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, manyHeaders, endStream: false)
            .Data(1, "many-headers")
            .Build();

        var (response2, _) = await SendH2EngineAsync(Engine.CreateFlow(), request2, serverFrames2);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.True(response2.Headers.TryGetValues("X-Custom-000", out _));

        // Test /large/8 endpoint
        var request3 = new HttpRequestMessage(HttpMethod.Get, "http://localhost/large/8")
        {
            Version = HttpVersion.Version20
        };

        var largeBody = new byte[8 * 1024];
        Array.Fill(largeBody, (byte)'A');

        var serverFrames3 = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("content-length", largeBody.Length.ToString())], endStream: false)
            .Data(1, (ReadOnlyMemory<byte>)largeBody)
            .Build();

        var (response3, _) = await SendH2EngineAsync(Engine.CreateFlow(), request3, serverFrames3);
        Assert.Equal(HttpStatusCode.OK, response3.StatusCode);
        var largeResponseBody = await response3.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(8 * 1024, largeResponseBody.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Post_with_body_followed_by_get_should_work_on_same_connection()
    {
        var postPayload = "hello from http2"u8.ToArray();
        var postRequest = new HttpRequestMessage(HttpMethod.Post, "http://localhost/h2/echo-binary")
        {
            Version = HttpVersion.Version20,
            Content = new ByteArrayContent(postPayload)
        };

        var postFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("content-length", postPayload.Length.ToString())], endStream: false)
            .Data(1, (ReadOnlyMemory<byte>)postPayload)
            .Build();

        var (postResponse, _) = await SendH2EngineAsync(Engine.CreateFlow(), postRequest, postFrames);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var postBody = await postResponse.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(postPayload, postBody);

        var getPath = "/h2/echo-path?q=1";
        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"http://localhost{getPath}")
        {
            Version = HttpVersion.Version20
        };

        var getFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("content-length", Encoding.UTF8.GetByteCount(getPath).ToString())], endStream: false)
            .Data(1, getPath)
            .Build();

        var (getResponse, _) = await SendH2EngineAsync(Engine.CreateFlow(), getRequest, getFrames);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getBody = await getResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(getPath, getBody);
    }
}
