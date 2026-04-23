using System.Net;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.IO;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H2;

public sealed class ExpectContinueSpec : AcceptanceTestBase
{
    private static BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed>
        CreateExpectContinueEngine()
    {
        var stage = new ExpectContinueBidiStage(Expect100Policy.Default);
        return BidiFlow.FromGraph(stage).Atop(CreateHttp20Engine().CreateFlow());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-10.1.1")]
    public async Task SmallBody_should_be_sent_without_Expect_header()
    {
        const string body = "hello";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/expect/echo")
        {
            Version = HttpVersion.Version20,
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("content-length", Encoding.UTF8.GetByteCount(body).ToString())], endStream: false)
            .Data(1, body)
            .Build();

        var (response, _) = await SendH2EngineAsync(CreateExpectContinueEngine(), request, serverFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(body, responseBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-10.1.1")]
    public async Task LargeBody_should_be_sent_without_100_continue()
    {
        var body = new string('x', 2048);
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/expect/large")
        {
            Version = HttpVersion.Version20,
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("content-length", Encoding.UTF8.GetByteCount(body).ToString())], endStream: false)
            .Data(1, body)
            .Build();

        var (response, _) = await SendH2EngineAsync(CreateExpectContinueEngine(), request, serverFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(body, responseBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-10.1.1")]
    public async Task Server_rejection_should_return_417()
    {
        var body = new string('x', 2048);
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/expect/reject")
        {
            Version = HttpVersion.Version20,
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 417, endStream: true)
            .Build();

        var (response, _) = await SendH2EngineAsync(CreateExpectContinueEngine(), request, serverFrames);

        Assert.Equal(HttpStatusCode.ExpectationFailed, response.StatusCode);
    }
}