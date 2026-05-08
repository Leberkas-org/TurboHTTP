using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.TLS;

public sealed class SmokeSpec : AcceptanceTestBase
{
    private static Http11Engine Engine =>
        new(new TurboClientOptions());

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.3")]
    public async Task Smoke_should_send_get_request_to_hello_and_receive_200_with_hello_world_body()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/hello")
        {
            Version = HttpVersion.Version11
        };

        const string body = "Hello World";
        var raw = $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\n\r\n{body}";
        var responseBytes = Encoding.Latin1.GetBytes(raw);

        var fake = CreateScriptedConnection((_, _) => responseBytes);
        var flow = Engine.CreateFlow().Join(fake.AsFlow());

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", responseBody);
    }
}
