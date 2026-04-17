using System.Net;
using TurboHTTP.Internal;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H2;

public sealed class EdgeCaseSpec : AcceptanceTestBase
{
    private static Http20Engine Engine => new(new Http2Options().ToEngineOptions());

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Large_binary_post_should_be_echoed_correctly()
    {
        var payload = new byte[60 * 1024];
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
    public async Task Many_custom_response_headers_should_be_all_accessible()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/h2/many-headers")
        {
            Version = HttpVersion.Version20
        };

        var headers = new List<(string Name, string Value)>();
        for (var i = 0; i < 20; i++)
        {
            headers.Add(($"x-custom-{i:D3}", $"value-{i:D3}"));
        }
        headers.Add(("content-length", "12"));

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, headers, endStream: false)
            .Data(1, "many-headers")
            .Build();

        var (response, _) = await SendH2EngineAsync(Engine.CreateFlow(), request, serverFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("many-headers", body);

        for (var i = 0; i < 20; i++)
        {
            Assert.True(
                response.Headers.TryGetValues($"X-Custom-{i:D3}", out var values),
                $"Header X-Custom-{i:D3} missing");
            Assert.Equal($"value-{i:D3}", string.Join("", values!));
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Large_hpack_headers_with_body_should_be_received_correctly()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/h2/large-headers/4")
        {
            Version = HttpVersion.Version20
        };

        var headers = new List<(string Name, string Value)>();
        for (var i = 0; i < 10; i++)
        {
            headers.Add(($"x-large-{i:D2}", new string((char)('A' + i), 90)));
        }

        var bodyBytes = new byte[4 * 1024];
        Array.Fill(bodyBytes, (byte)'X');
        headers.Add(("content-length", bodyBytes.Length.ToString()));

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, headers, endStream: false)
            .Data(1, (ReadOnlyMemory<byte>)bodyBytes)
            .Build();

        var (response, _) = await SendH2EngineAsync(Engine.CreateFlow(), request, serverFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(4 * 1024, bytes.Length);

        Assert.True(response.Headers.TryGetValues("X-Large-00", out var headerValues));
        Assert.Equal(90, string.Join("", headerValues!).Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Stream_priority_route_should_return_expected_payload_bytes()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/h2/priority/16")
        {
            Version = HttpVersion.Version20
        };

        var bodyBytes = new byte[16 * 1024];
        Array.Fill(bodyBytes, (byte)'P');

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("content-length", bodyBytes.Length.ToString())], endStream: false)
            .Data(1, (ReadOnlyMemory<byte>)bodyBytes)
            .Build();

        var (response, _) = await SendH2EngineAsync(Engine.CreateFlow(), request, serverFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(16 * 1024, bytes.Length);
        Assert.All(bytes, b => Assert.Equal((byte)'P', b));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Echo_path_should_return_path_and_query_string()
    {
        var path = "/h2/echo-path?foo=bar&baz=qux";
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost{path}")
        {
            Version = HttpVersion.Version20
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("content-length", System.Text.Encoding.UTF8.GetByteCount(path).ToString())], endStream: false)
            .Data(1, path)
            .Build();

        var (response, _) = await SendH2EngineAsync(Engine.CreateFlow(), request, serverFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(path, body);
    }
}
