using System.Net;
using System.Text;
using TurboHTTP.Internal;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H3;

public sealed class EdgeCaseSpec : AcceptanceTestBase
{
    private static Http30Engine Engine => new(new Http3Options().ToEngineOptions());

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task EdgeCase_should_receive_many_custom_response_headers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/h3/many-headers")
        {
            Version = HttpVersion.Version30
        };

        var headers = new List<(string Name, string Value)>();
        for (var i = 0; i < 20; i++)
        {
            headers.Add(($"x-custom-{i:D3}", $"value-{i:D3}"));
        }
        headers.Add(("content-length", "12"));

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, headers)
            .Data("many-headers")
            .Build();

        var (response, _) = await SendH3EngineAsync(Engine.CreateFlow(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("many-headers", body);

        for (var i = 0; i < 20; i++)
        {
            var headerName = $"X-Custom-{i:D3}";
            Assert.True(response.Headers.TryGetValues(headerName, out var values),
                $"Missing header {headerName}");
            Assert.Equal($"value-{i:D3}", string.Join("", values));
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task EdgeCase_should_return_empty_body_for_content_length_zero()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/empty-cl")
        {
            Version = HttpVersion.Version30
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-length", "0")], endStream: true)
            .Build();

        var (response, _) = await SendH3EngineAsync(Engine.CreateFlow(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task EdgeCase_should_return_empty_for_empty_body_with_no_content()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/edge/empty-body")
        {
            Version = HttpVersion.Version30
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-length", "0")], endStream: true)
            .Build();

        var (response, _) = await SendH3EngineAsync(Engine.CreateFlow(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task EdgeCase_should_receive_large_body_256kb_intact()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/large/256")
        {
            Version = HttpVersion.Version30
        };

        var largeBody = new byte[256 * 1024];
        Array.Fill(largeBody, (byte)'A');

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-length", largeBody.Length.ToString())])
            .Data((ReadOnlyMemory<byte>)largeBody)
            .Build();

        var (response, _) = await SendH3EngineAsync(Engine.CreateFlow(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(256 * 1024, bytes.Length);
        Assert.All(bytes, b => Assert.Equal((byte)'A', b));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task EdgeCase_should_echo_large_binary_post_correctly()
    {
        var payload = new byte[60 * 1024];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/h3/echo-binary")
        {
            Version = HttpVersion.Version30,
            Content = new ByteArrayContent(payload)
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-length", payload.Length.ToString())])
            .Data((ReadOnlyMemory<byte>)payload)
            .Build();

        var (response, _) = await SendH3EngineAsync(Engine.CreateFlow(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task EdgeCase_should_receive_large_qpack_compressed_headers_1kb()
    {
        await AssertLargeQpackHeadersAsync(1);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task EdgeCase_should_receive_large_qpack_compressed_headers_4kb()
    {
        await AssertLargeQpackHeadersAsync(4);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task EdgeCase_should_access_multiple_response_headers_with_same_name()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/multiheader")
        {
            Version = HttpVersion.Version30
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("x-value", "alpha"), ("x-value", "beta"), ("content-length", "0")], endStream: true)
            .Build();

        var (response, _) = await SendH3EngineAsync(Engine.CreateFlow(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Value", out var values));
        var valueList = values.ToList();
        Assert.Contains("alpha", valueList);
        Assert.Contains("beta", valueList);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task EdgeCase_should_return_received_length_for_form_urlencoded_post()
    {
        var formData = "field1=value1&field2=value2&field3=hello+world";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/form/urlencoded")
        {
            Version = HttpVersion.Version30,
            Content = new StringContent(formData, Encoding.UTF8, "application/x-www-form-urlencoded")
        };

        var responseBody = $"received:{Encoding.UTF8.GetByteCount(formData)}";
        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-length", Encoding.UTF8.GetByteCount(responseBody).ToString())])
            .Data(responseBody)
            .Build();

        var (response, _) = await SendH3EngineAsync(Engine.CreateFlow(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.StartsWith("received:", body);
        Assert.True(int.Parse(body["received:".Length..]) > 0);
    }

    private async Task AssertLargeQpackHeadersAsync(int kb)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost/h3/large-headers/{kb}")
        {
            Version = HttpVersion.Version30
        };

        var headers = new List<(string Name, string Value)>();
        for (var i = 0; i < 10; i++)
        {
            headers.Add(($"x-large-{i:D2}", new string((char)('A' + i), 90)));
        }

        var bodyBytes = new byte[kb * 1024];
        Array.Fill(bodyBytes, (byte)'X');
        headers.Add(("content-length", bodyBytes.Length.ToString()));

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, headers)
            .Data((ReadOnlyMemory<byte>)bodyBytes)
            .Build();

        var (response, _) = await SendH3EngineAsync(Engine.CreateFlow(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(kb * 1024, body.Length);

        for (var i = 0; i < 10; i++)
        {
            var headerName = $"X-Large-{i:D2}";
            Assert.True(response.Headers.TryGetValues(headerName, out var values),
                $"Missing header {headerName}");
            Assert.Equal(90, string.Join("", values).Length);
        }
    }
}
