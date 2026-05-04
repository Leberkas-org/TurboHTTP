using System.Net;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H10;

public sealed class SmokeSpec : AcceptanceTestBase
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.1")]
    public async Task SmokeTest_should_return_200_hello_world()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
        {
            Version = HttpVersion.Version10
        };

        const string body = "Hello World";
        var raw = $"HTTP/1.0 200 OK\r\nContent-Length: {body.Length}\r\n\r\n{body}";
        var responseBytes = Encoding.Latin1.GetBytes(raw);

        var fake = CreateScriptedConnection((_, _) => responseBytes);
        var flow = CreateHttp10Engine().CreateFlow().Join(fake.AsFlow());

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
