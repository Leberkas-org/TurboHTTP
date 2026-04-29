using System.Net;
using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H2;

public sealed class ErrorHandlingSpec : AcceptanceTestBase
{
    [Fact(Timeout = 15000)]
    [Trait("RFC", "RFC9113-5.4.2")]
    public async Task RstStream_should_raise_exception_on_abort()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/h2/abort")
        {
            Version = HttpVersion.Version20
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .RstStream(1, Http2ErrorCode.Cancel)
            .Build();

        var fake = new H2EngineFakeConnectionStage(serverFrames);
        var flow = CreateHttp20Engine().CreateFlow().Join(Flow.FromGraph<ITransportOutbound, ITransportInbound, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Delay_should_complete_after_server_wait()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/h2/delay/500")
        {
            Version = HttpVersion.Version20
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("content-length", "7")], endStream: false)
            .Data(1, "delayed")
            .Build();

        var (response, _) = await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, serverFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("delayed", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Timeout_should_cancel_in_flight_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/h2/delay/10000")
        {
            Version = HttpVersion.Version20
        };

        // Server sends SETTINGS but never responds with HEADERS â€” simulates timeout
        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .Build();

        var fake = new H2EngineFakeConnectionStage(serverFrames);
        var flow = CreateHttp20Engine().CreateFlow().Join(Flow.FromGraph<ITransportOutbound, ITransportInbound, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Status_4xx_should_be_returned_as_response_400()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/status/400")
        {
            Version = HttpVersion.Version20
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 400, endStream: true)
            .Build();

        var (response, _) = await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, serverFrames);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Status_4xx_should_be_returned_as_response_401()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/status/401")
        {
            Version = HttpVersion.Version20
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 401, endStream: true)
            .Build();

        var (response, _) = await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, serverFrames);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Status_4xx_should_be_returned_as_response_403()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/status/403")
        {
            Version = HttpVersion.Version20
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 403, endStream: true)
            .Build();

        var (response, _) = await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, serverFrames);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Status_4xx_should_be_returned_as_response_404()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/status/404")
        {
            Version = HttpVersion.Version20
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 404, endStream: true)
            .Build();

        var (response, _) = await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, serverFrames);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Status_4xx_should_be_returned_as_response_429()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/status/429")
        {
            Version = HttpVersion.Version20
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 429, endStream: true)
            .Build();

        var (response, _) = await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, serverFrames);
        Assert.Equal((HttpStatusCode)429, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Status_5xx_should_be_returned_as_response_500()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/status/500")
        {
            Version = HttpVersion.Version20
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 500, endStream: true)
            .Build();

        var (response, _) = await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, serverFrames);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Status_5xx_should_be_returned_as_response_502()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/status/502")
        {
            Version = HttpVersion.Version20
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 502, endStream: true)
            .Build();

        var (response, _) = await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, serverFrames);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Status_5xx_should_be_returned_as_response_503()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/status/503")
        {
            Version = HttpVersion.Version20
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 503, endStream: true)
            .Build();

        var (response, _) = await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, serverFrames);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Many_custom_response_headers_should_be_decoded_correctly()
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

        var (response, _) = await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, serverFrames);

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
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Binary_body_should_roundtrip()
    {
        var payload = new byte[256];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)i;
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

        var (response, _) = await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, serverFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Large_hpack_compressed_headers_should_be_received_correctly_1kb()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/h2/large-headers/1")
        {
            Version = HttpVersion.Version20
        };

        var headers = new List<(string Name, string Value)>();
        for (var i = 0; i < 10; i++)
        {
            headers.Add(($"x-large-{i:D2}", new string((char)('A' + i), 90)));
        }

        var bodyBytes = new byte[1 * 1024];
        Array.Fill(bodyBytes, (byte)'X');
        headers.Add(("content-length", bodyBytes.Length.ToString()));

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, headers, endStream: false)
            .Data(1, (ReadOnlyMemory<byte>)bodyBytes)
            .Build();

        var (response, _) = await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, serverFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1 * 1024, body.Length);

        for (var i = 0; i < 10; i++)
        {
            var headerName = $"X-Large-{i:D2}";
            Assert.True(response.Headers.TryGetValues(headerName, out var values),
                $"Missing header {headerName}");
            Assert.Equal(90, string.Join("", values).Length);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Large_hpack_compressed_headers_should_be_received_correctly_4kb()
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

        var (response, _) = await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, serverFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(4 * 1024, body.Length);

        for (var i = 0; i < 10; i++)
        {
            var headerName = $"X-Large-{i:D2}";
            Assert.True(response.Headers.TryGetValues(headerName, out var values),
                $"Missing header {headerName}");
            Assert.Equal(90, string.Join("", values).Length);
        }
    }
}
