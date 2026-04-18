using System.Net;
using System.Text;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H2;

public sealed class RequestFormatSpec : AcceptanceTestBase
{
    private static byte[] StandardServerFrames(int streamId = 1, string body = "ok") =>
        new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(streamId, 200, [("content-length", Encoding.UTF8.GetByteCount(body).ToString())], endStream: false)
            .Data(streamId, body)
            .Build();

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public async Task Request_should_emit_settings_frame()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/settings")
        {
            Version = HttpVersion.Version20
        };

        var (_, outboundFrames) =
            await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, StandardServerFrames());

        Assert.Contains(outboundFrames, f => f is SettingsFrame { IsAck: false });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public async Task Request_should_emit_settings_ack()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/ack")
        {
            Version = HttpVersion.Version20
        };

        var (_, outboundFrames) =
            await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, StandardServerFrames());

        Assert.Contains(outboundFrames, f => f is SettingsFrame { IsAck: true });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3.1")]
    public async Task Get_request_should_contain_method_pseudo_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/method")
        {
            Version = HttpVersion.Version20
        };

        var (_, outboundFrames) =
            await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, StandardServerFrames());

        var headersFrame = outboundFrames.OfType<HeadersFrame>().First();
        var decoder = new HpackDecoder();
        var headers = decoder.Decode(headersFrame.HeaderBlockFragment.Span);

        Assert.Contains(headers, h => h.Name == ":method" && h.Value == "GET");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3.1")]
    public async Task Get_request_should_contain_path_pseudo_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/some/path")
        {
            Version = HttpVersion.Version20
        };

        var (_, outboundFrames) =
            await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, StandardServerFrames());

        var headersFrame = outboundFrames.OfType<HeadersFrame>().First();
        var decoder = new HpackDecoder();
        var headers = decoder.Decode(headersFrame.HeaderBlockFragment.Span);

        Assert.Contains(headers, h => h.Name == ":path" && h.Value == "/some/path");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3.1")]
    public async Task Get_request_should_contain_scheme_pseudo_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/scheme")
        {
            Version = HttpVersion.Version20
        };

        var (_, outboundFrames) =
            await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, StandardServerFrames());

        var headersFrame = outboundFrames.OfType<HeadersFrame>().First();
        var decoder = new HpackDecoder();
        var headers = decoder.Decode(headersFrame.HeaderBlockFragment.Span);

        Assert.Contains(headers, h => h.Name == ":scheme" && h.Value == "http");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3.1")]
    public async Task Get_request_should_contain_authority_pseudo_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/authority")
        {
            Version = HttpVersion.Version20
        };

        var (_, outboundFrames) =
            await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, StandardServerFrames());

        var headersFrame = outboundFrames.OfType<HeadersFrame>().First();
        var decoder = new HpackDecoder();
        var headers = decoder.Decode(headersFrame.HeaderBlockFragment.Span);

        Assert.Contains(headers, h => h.Name == ":authority" && h.Value == "example.com");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Get_request_headers_frame_should_have_end_stream()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/end-stream")
        {
            Version = HttpVersion.Version20
        };

        var (_, outboundFrames) =
            await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, StandardServerFrames());

        var headersFrame = outboundFrames.OfType<HeadersFrame>().First();
        Assert.True(headersFrame.EndStream, "GET request HEADERS frame must have END_STREAM set");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Post_request_should_emit_headers_then_data()
    {
        var body = "post payload";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/post")
        {
            Version = HttpVersion.Version20,
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

        var (_, outboundFrames) =
            await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, StandardServerFrames());

        var headersFrame = outboundFrames.OfType<HeadersFrame>().First();
        Assert.False(headersFrame.EndStream, "POST request HEADERS frame must NOT have END_STREAM");

        var dataFrames = outboundFrames.OfType<DataFrame>().ToList();
        Assert.NotEmpty(dataFrames);
        Assert.True(dataFrames[^1].EndStream, "Last DATA frame must have END_STREAM");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3.1")]
    public async Task Post_request_should_contain_method_pseudo_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/post-method")
        {
            Version = HttpVersion.Version20,
            Content = new StringContent("data", Encoding.UTF8, "text/plain")
        };

        var (_, outboundFrames) =
            await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, StandardServerFrames());

        var headersFrame = outboundFrames.OfType<HeadersFrame>().First();
        var decoder = new HpackDecoder();
        var headers = decoder.Decode(headersFrame.HeaderBlockFragment.Span);

        Assert.Contains(headers, h => h.Name == ":method" && h.Value == "POST");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3.1")]
    public async Task Request_should_place_pseudo_headers_before_regular_headers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/order")
        {
            Version = HttpVersion.Version20
        };
        request.Headers.TryAddWithoutValidation("X-Custom", "value");

        var (_, outboundFrames) =
            await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, StandardServerFrames());

        var headersFrame = outboundFrames.OfType<HeadersFrame>().First();
        var decoder = new HpackDecoder();
        var headers = decoder.Decode(headersFrame.HeaderBlockFragment.Span);

        var lastPseudoIndex = -1;
        var firstRegularIndex = int.MaxValue;
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Name.StartsWith(':'))
            {
                lastPseudoIndex = i;
            }
            else if (firstRegularIndex == int.MaxValue)
            {
                firstRegularIndex = i;
            }
        }

        Assert.True(lastPseudoIndex < firstRegularIndex,
            "All pseudo-headers must precede regular headers");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Request_should_use_odd_stream_ids()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/stream-id")
        {
            Version = HttpVersion.Version20
        };

        var (_, outboundFrames) =
            await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, StandardServerFrames());

        var headersFrame = outboundFrames.OfType<HeadersFrame>().First();
        Assert.True(headersFrame.StreamId % 2 == 1, "Client-initiated stream IDs must be odd");
    }
}