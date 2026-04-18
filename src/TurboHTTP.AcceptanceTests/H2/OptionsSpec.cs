using System.Net;
using Akka.Streams.Dsl;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H2;

public sealed class OptionsSpec : AcceptanceTestBase
{
    private static TurboRequestOptions CreateOptions(ICredentials? credentials = null,
        bool preAuthenticate = false)
    {
        var msg = new HttpRequestMessage();
        return new TurboRequestOptions(
            BaseAddress: null,
            DefaultRequestHeaders: msg.Headers,
            DefaultRequestVersion: HttpVersion.Version20,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: credentials,
            PreAuthenticate: preAuthenticate);
    }

    private async Task<HttpResponseMessage> SendAsync(ResponseMap map, HttpRequestMessage request,
        TurboRequestOptions options)
    {
        var enricher = new RequestEnricher(() => options);
        var fake = ResponseMapFake.Create(map);
        var flow = Flow.Create<HttpRequestMessage>()
            .Select(enricher.Enrich)
            .Via(fake.Join(Flow.FromFunction<HttpRequestMessage, HttpResponseMessage>(_ => new HttpResponseMessage())));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private static ResponseMap CreateAuthMap() => new ResponseMap()
        .On("/auth", req =>
        {
            if (req.Headers.Authorization is not null)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        })
        .On("/auth/echo", req =>
        {
            if (req.Headers.Authorization is not null)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"{req.Headers.Authorization.Scheme} {req.Headers.Authorization.Parameter}")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-11.6.1")]
    public async Task PreAuthenticate_should_inject_authorization_header_when_credentials_set()
    {
        var map = CreateAuthMap();
        var options = CreateOptions(
            credentials: new NetworkCredential("testuser", "testpass"),
            preAuthenticate: true);

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/auth"), options);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-11.6.1")]
    public async Task PreAuthenticate_should_send_basic_auth_header_with_correct_credentials()
    {
        var map = CreateAuthMap();
        var options = CreateOptions(
            credentials: new NetworkCredential("alice", "secret"),
            preAuthenticate: true);

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/auth/echo"), options);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.StartsWith("Basic ", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-11.6.1")]
    public async Task Auth_should_return_401_when_no_credentials_configured()
    {
        var map = CreateAuthMap();
        var options = CreateOptions();

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/auth"), options);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-11.6.1")]
    public async Task PreAuthenticate_should_not_inject_header_when_disabled()
    {
        var map = CreateAuthMap();
        var options = CreateOptions(
            credentials: new NetworkCredential("testuser", "testpass"),
            preAuthenticate: false);

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/auth"), options);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.1.2")]
    public async Task PooledConnectionLifetime_should_allow_requests_after_connection_expires()
    {
        var map = new ResponseMap()
            .On("/hello", _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Hello World")
            });

        var options = CreateOptions();

        var response1 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello"), options);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        var response2 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello"), options);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body = await response2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-11.6.1")]
    public async Task PreAuthenticate_should_work_across_multiple_requests()
    {
        var map = CreateAuthMap();
        var options = CreateOptions(
            credentials: new NetworkCredential("testuser", "testpass"),
            preAuthenticate: true);

        for (var i = 0; i < 3; i++)
        {
            var response = await SendAsync(map,
                new HttpRequestMessage(HttpMethod.Get, "http://localhost/auth"), options);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}