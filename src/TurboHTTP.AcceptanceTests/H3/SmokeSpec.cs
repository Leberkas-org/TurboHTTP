using System.Net;
using System.Text;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H3;

public sealed class SmokeSpec : AcceptanceTestBase
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Basic_get_should_return_200_with_body()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
        {
            Version = HttpVersion.Version30
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-length", "11")])
            .Data("Hello World")
            .Build();

        var (response, _) =
            await SendH3EngineAsync(CreateHttp30Engine().CreateFlow(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Post_should_echo_request_body()
    {
        var payload = "HTTP/3 smoke test payload";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/echo")
        {
            Version = HttpVersion.Version30,
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-length", Encoding.UTF8.GetByteCount(payload).ToString())])
            .Data(payload)
            .Build();

        var (response, _) =
            await SendH3EngineAsync(CreateHttp30Engine().CreateFlow(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Status_code_should_match_requested_code_200()
    {
        await AssertStatusCodeAsync(200);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Status_code_should_match_requested_code_201()
    {
        await AssertStatusCodeAsync(201);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Status_code_should_match_requested_code_204()
    {
        await AssertStatusCodeAsync(204);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Status_code_should_match_requested_code_400()
    {
        await AssertStatusCodeAsync(400);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Status_code_should_match_requested_code_404()
    {
        await AssertStatusCodeAsync(404);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Status_code_should_match_requested_code_500()
    {
        await AssertStatusCodeAsync(500);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Custom_headers_should_round_trip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/headers/echo")
        {
            Version = HttpVersion.Version30
        };
        request.Headers.TryAddWithoutValidation("X-Smoke-Test", "h3-value");

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("x-smoke-test", "h3-value"), ("content-length", "0")], endStream: true)
            .Build();

        var (response, _) =
            await SendH3EngineAsync(CreateHttp30Engine().CreateFlow(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Smoke-Test", out var values));
        Assert.Equal("h3-value", values.Single());
    }

    private async Task AssertStatusCodeAsync(int expectedCode)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost/status/{expectedCode}")
        {
            Version = HttpVersion.Version30
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(expectedCode, endStream: true)
            .Build();

        var (response, _) =
            await SendH3EngineAsync(CreateHttp30Engine().CreateFlow(), request, controlFrames, responseFrames);

        Assert.Equal((HttpStatusCode)expectedCode, response.StatusCode);
    }
}