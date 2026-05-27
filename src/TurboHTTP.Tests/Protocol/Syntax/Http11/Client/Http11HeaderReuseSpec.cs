using System.Text;
using Akka.Actor;
using TurboHTTP.Protocol.Syntax.Http11.Client;
using TurboHTTP.Protocol.Syntax.Http11.Options;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Client;

public sealed class Http11HeaderReuseSpec
{
    [Fact(Timeout = 5000)]
    public void Encode_should_produce_valid_output_on_second_call()
    {
        var encoder = new Http11ClientEncoder(Http11ClientEncoderOptions.Default);

        var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/first");
        var buffer1 = new byte[4 * 1024];
        var written1 = encoder.Encode(buffer1, request1, ActorRefs.Nobody);
        var result1 = Encoding.ASCII.GetString(buffer1, 0, written1);

        var request2 = new HttpRequestMessage(HttpMethod.Post, "http://example.com/second");
        var buffer2 = new byte[4 * 1024];
        var written2 = encoder.Encode(buffer2, request2, ActorRefs.Nobody);
        var result2 = Encoding.ASCII.GetString(buffer2, 0, written2);

        Assert.Contains("GET /first HTTP/1.1", result1);
        Assert.Contains("POST /second HTTP/1.1", result2);
        Assert.Contains("Host: example.com", result1);
        Assert.Contains("Host: example.com", result2);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_not_leak_headers_between_calls()
    {
        var encoder = new Http11ClientEncoder(Http11ClientEncoderOptions.Default);

        var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request1.Headers.Add("X-Custom", "value1");
        var buffer1 = new byte[4 * 1024];
        var written1 = encoder.Encode(buffer1, request1, ActorRefs.Nobody);
        var result1 = Encoding.ASCII.GetString(buffer1, 0, written1);

        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var buffer2 = new byte[4 * 1024];
        var written2 = encoder.Encode(buffer2, request2, ActorRefs.Nobody);
        var result2 = Encoding.ASCII.GetString(buffer2, 0, written2);

        Assert.Contains("X-Custom: value1", result1);
        Assert.DoesNotContain("X-Custom", result2);
    }
}
