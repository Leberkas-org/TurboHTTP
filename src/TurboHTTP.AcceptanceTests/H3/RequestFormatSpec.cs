using System.Net;
using System.Text;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Protocol.Http3.Qpack;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H3;

public sealed class RequestFormatSpec : AcceptanceTestBase
{
    private static Http30Engine Engine => new(new Http3Options().ToEngineOptions());

    private static byte[] ControlFrames() =>
        new H3ResponseBuilder().Settings().Build();

    private static byte[] ResponseFrames(string body = "ok") =>
        new H3ResponseBuilder()
            .Headers(200, [("content-length", Encoding.UTF8.GetByteCount(body).ToString())])
            .Data(body)
            .Build();

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public async Task Request_should_emit_settings_frame_on_control_stream()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/settings")
        {
            Version = HttpVersion.Version30
        };

        var (_, outboundFrames) = await SendH3EngineAsync(
            Engine.CreateFlow(), request, ControlFrames(), ResponseFrames());

        Assert.Contains(outboundFrames, f => f is Http3SettingsFrame);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Get_request_should_contain_method_pseudo_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/method")
        {
            Version = HttpVersion.Version30
        };

        var (_, outboundFrames) = await SendH3EngineAsync(
            Engine.CreateFlow(), request, ControlFrames(), ResponseFrames());

        var headersFrame = outboundFrames.OfType<Http3HeadersFrame>().First();
        var decoder = new QpackDecoder();
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h.Name == ":method" && h.Value == "GET");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Get_request_should_contain_path_pseudo_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/some/path")
        {
            Version = HttpVersion.Version30
        };

        var (_, outboundFrames) = await SendH3EngineAsync(
            Engine.CreateFlow(), request, ControlFrames(), ResponseFrames());

        var headersFrame = outboundFrames.OfType<Http3HeadersFrame>().First();
        var decoder = new QpackDecoder();
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h.Name == ":path" && h.Value == "/some/path");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Get_request_should_contain_scheme_pseudo_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/scheme")
        {
            Version = HttpVersion.Version30
        };

        var (_, outboundFrames) = await SendH3EngineAsync(
            Engine.CreateFlow(), request, ControlFrames(), ResponseFrames());

        var headersFrame = outboundFrames.OfType<Http3HeadersFrame>().First();
        var decoder = new QpackDecoder();
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h.Name == ":scheme" && h.Value == "http");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Get_request_should_contain_authority_pseudo_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/authority")
        {
            Version = HttpVersion.Version30
        };

        var (_, outboundFrames) = await SendH3EngineAsync(
            Engine.CreateFlow(), request, ControlFrames(), ResponseFrames());

        var headersFrame = outboundFrames.OfType<Http3HeadersFrame>().First();
        var decoder = new QpackDecoder();
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h.Name == ":authority" && h.Value == "example.com");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Post_request_should_emit_headers_then_data()
    {
        var body = "post payload";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/post")
        {
            Version = HttpVersion.Version30,
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

        var (_, outboundFrames) = await SendH3EngineAsync(
            Engine.CreateFlow(), request, ControlFrames(), ResponseFrames());

        var headersFrame = outboundFrames.OfType<Http3HeadersFrame>().FirstOrDefault();
        Assert.NotNull(headersFrame);

        var dataFrames = outboundFrames.OfType<Http3DataFrame>().ToList();
        Assert.NotEmpty(dataFrames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Post_request_should_contain_method_pseudo_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/post-method")
        {
            Version = HttpVersion.Version30,
            Content = new StringContent("data", Encoding.UTF8, "text/plain")
        };

        var (_, outboundFrames) = await SendH3EngineAsync(
            Engine.CreateFlow(), request, ControlFrames(), ResponseFrames());

        var headersFrame = outboundFrames.OfType<Http3HeadersFrame>().First();
        var decoder = new QpackDecoder();
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h.Name == ":method" && h.Value == "POST");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public async Task Request_should_place_pseudo_headers_before_regular_headers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/order")
        {
            Version = HttpVersion.Version30
        };
        request.Headers.TryAddWithoutValidation("X-Custom", "value");

        var (_, outboundFrames) = await SendH3EngineAsync(
            Engine.CreateFlow(), request, ControlFrames(), ResponseFrames());

        var headersFrame = outboundFrames.OfType<Http3HeadersFrame>().First();
        var decoder = new QpackDecoder();
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

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
}
