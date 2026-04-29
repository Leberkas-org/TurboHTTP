using System.Net;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H11;

public sealed class EdgeCaseSpec : AcceptanceTestBase
{
    private static Http11Engine Engine =>
        new(new TurboClientOptions());

    private static byte[] BuildResponse(byte[] body, HttpStatusCode status = HttpStatusCode.OK,
        string? extraHeaders = null)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {(int)status} {status}\r\n");
        sb.Append($"Content-Length: {body.Length}\r\n");
        if (extraHeaders is not null)
        {
            sb.Append(extraHeaders);
        }

        sb.Append("\r\n");

        var headerBytes = Encoding.Latin1.GetBytes(sb.ToString());
        var result = new byte[headerBytes.Length + body.Length];
        headerBytes.CopyTo(result, 0);
        body.CopyTo(result, headerBytes.Length);
        return result;
    }

    private static byte[] BuildResponse(string body, HttpStatusCode status = HttpStatusCode.OK,
        string? extraHeaders = null)
    {
        return BuildResponse(Encoding.Latin1.GetBytes(body), status, extraHeaders);
    }

    private static byte[] BuildChunkedResponse(string body, string? trailerHeaders = null)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 200 OK\r\n");
        sb.Append("Transfer-Encoding: chunked\r\n");
        sb.Append("\r\n");
        sb.Append($"{body.Length:X}\r\n");
        sb.Append(body);
        sb.Append("\r\n");
        sb.Append("0\r\n");
        if (trailerHeaders is not null)
        {
            sb.Append(trailerHeaders);
        }

        sb.Append("\r\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    private async Task<HttpResponseMessage> SendScriptedAsync(HttpRequestMessage request,
        Func<int, byte[], byte[]?> factory)
    {
        var fake = new ScriptedFakeConnectionStage(factory);
        var flow = Engine.CreateFlow().Join(Flow.FromGraph<ITransportOutbound, ITransportInbound, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1")]
    public async Task EdgeCase_should_deliver_chunked_response_with_trailers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/chunked/trailer")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request,
            (_, _) => BuildChunkedResponse("chunked-with-trailer", "X-Trailer: done\r\n"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("chunked-with-trailer", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1")]
    public async Task EdgeCase_should_deliver_all_bytes_with_chunked_exact_boundaries()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/chunked/exact/5/1024")
        {
            Version = HttpVersion.Version11
        };

        var chunkData = new byte[1024];
        Array.Fill(chunkData, (byte)'B');
        var chunkHex = "400"; // 1024 in hex

        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 200 OK\r\n");
        sb.Append("Transfer-Encoding: chunked\r\n");
        sb.Append("\r\n");
        for (var i = 0; i < 5; i++)
        {
            sb.Append($"{chunkHex}\r\n");
            sb.Append(new string('B', 1024));
            sb.Append("\r\n");
        }

        sb.Append("0\r\n\r\n");

        var responseBytes = Encoding.Latin1.GetBytes(sb.ToString());

        var response = await SendScriptedAsync(request, (_, _) => responseBytes);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(5 * 1024, bytes.Length);
        Assert.All(bytes, b => Assert.Equal((byte)'B', b));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1.2")]
    public async Task EdgeCase_should_receive_chunked_response_with_md5_trailer()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/chunked/md5")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request,
            (_, _) => BuildChunkedResponse("checksum-body", "Content-MD5: fake-md5\r\n"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("checksum-body", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.3")]
    public async Task EdgeCase_should_echo_post_chunked_request_body()
    {
        var payload = new string('Z', 4096);
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/echo/chunked")
        {
            Version = HttpVersion.Version11,
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };

        var response = await SendScriptedAsync(request, (_, _) => BuildResponse(payload));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, body);
    }

    [Fact(Timeout = 15000)]
    [Trait("RFC", "RFC9110-8.6")]
    public async Task EdgeCase_should_receive_large_body_256kb_intact()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/large/256")
        {
            Version = HttpVersion.Version11
        };

        var largeBody = new byte[256 * 1024];
        Array.Fill(largeBody, (byte)'A');

        var response = await SendScriptedAsync(request, (_, _) => BuildResponse(largeBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(256 * 1024, bytes.Length);
        Assert.All(bytes, b => Assert.Equal((byte)'A', b));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5")]
    public async Task EdgeCase_should_access_multiple_response_headers_with_same_name()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/multiheader")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request,
            (_, _) => BuildResponse("", extraHeaders: "X-Value: alpha\r\nX-Value: beta\r\n"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Value", out var values));
        var valueList = values.ToList();
        Assert.Contains("alpha", valueList);
        Assert.Contains("beta", valueList);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.3")]
    public async Task EdgeCase_should_return_received_length_for_form_urlencoded_post()
    {
        var formData = "field1=value1&field2=value2&field3=hello+world";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/form/urlencoded")
        {
            Version = HttpVersion.Version11,
            Content = new StringContent(formData, Encoding.UTF8, "application/x-www-form-urlencoded")
        };

        var responseBody = $"received:{formData.Length}";
        var response = await SendScriptedAsync(request, (_, _) => BuildResponse(responseBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.StartsWith("received:", body);
        Assert.True(int.Parse(body["received:".Length..]) > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.2")]
    public async Task EdgeCase_should_return_206_partial_content_for_range_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/range/64")
        {
            Version = HttpVersion.Version11
        };
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 99);

        var bodyBytes = new byte[100];
        for (var i = 0; i < bodyBytes.Length; i++)
        {
            bodyBytes[i] = (byte)(i % 256);
        }

        var response = await SendScriptedAsync(request,
            (_, _) => BuildResponse(bodyBytes, HttpStatusCode.PartialContent,
                "Content-Range: bytes 0-99/65536\r\n"));

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(100, bytes.Length);
        for (var i = 0; i < bytes.Length; i++)
        {
            Assert.Equal((byte)(i % 256), bytes[i]);
        }
    }
}
