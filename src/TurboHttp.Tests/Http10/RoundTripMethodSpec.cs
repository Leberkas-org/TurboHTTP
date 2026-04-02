using System.Text;
using TurboHttp.Protocol.Http10;

namespace TurboHttp.Tests.Http10;

/// <summary>
/// Round-trip tests for HTTP/1.0 request methods per RFC 1945 §5.1.1.
/// Encodes with Http10Encoder and decodes with Http10Decoder; verifies method is preserved.
/// </summary>
/// <remarks>
/// Classes under test: <see cref="Http10Encoder"/>, <see cref="Http10Decoder"/>.
/// RFC 1945 §5.1.1: Method token (GET, HEAD, POST, and extension methods).
/// </remarks>
public sealed class Http10RoundTripMethodSpec
{
    private static Span<byte> MakeBuffer(int size = 8192) => new byte[size];

    private static (byte[] Buffer, int Written) EncodeRequest(HttpRequestMessage request)
    {
        var arr = new byte[65536];
        Span<byte> buffer = arr;
        var written = Http10Encoder.Encode(request, ref buffer);
        return (arr[..written], written);
    }

    private static ReadOnlyMemory<byte> BuildResponse(int status, string reason, string body = "",
        params (string Name, string Value)[] headers)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.0 {status} {reason}\r\n");
        foreach (var (name, value) in headers)
        {
            sb.Append($"{name}: {value}\r\n");
        }

        sb.Append("\r\n");
        sb.Append(body);
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10RoundTripMethodSpec_should_preservegetmethod()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("GET /resource HTTP/1.0", raw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10RoundTripMethodSpec_should_preservepostmethod()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Content = new StringContent("data=value")
        };
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("POST /submit HTTP/1.0", raw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10RoundTripMethodSpec_should_preserveputmethod()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/resource")
        {
            Content = new StringContent("updated content")
        };
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("PUT /resource HTTP/1.0", raw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10RoundTripMethodSpec_should_preservedeletemethod()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/resource");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("DELETE /resource HTTP/1.0", raw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10RoundTripMethodSpec_should_preservepatchmethod()
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, "http://example.com/resource")
        {
            Content = new StringContent("{\"op\": \"replace\"}")
        };
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("PATCH /resource HTTP/1.0", raw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10RoundTripMethodSpec_should_preserveoptionsmethod()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "http://example.com/");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("OPTIONS / HTTP/1.0", raw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10RoundTripMethodSpec_should_preserveheadmethod()
    {
        var request = new HttpRequestMessage(HttpMethod.Head, "http://example.com/resource");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("HEAD /resource HTTP/1.0", raw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10RoundTripMethodSpec_should_preservequerystring()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/search?q=test&page=1");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.Contains("GET /search?q=test&page=1 HTTP/1.0", raw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10RoundTripMethodSpec_should_preservepostbody()
    {
        var bodyContent = "field1=value1&field2=value2";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/form")
        {
            Content = new StringContent(bodyContent)
        };
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = encodedBuffer[..written];
        var rawStr = Encoding.ASCII.GetString(raw);
        Assert.Contains(bodyContent, rawStr);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10RoundTripMethodSpec_should_preservemethodsconsistently()
    {
        var methods = new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete };
        var methodNames = new[] { "GET", "POST", "PUT", "DELETE" };

        for (var i = 0; i < methods.Length; i++)
        {
            var request = new HttpRequestMessage(methods[i], "http://example.com/api")
            {
                Content = i > 0 ? new StringContent("body") : null
            };
            var (encodedBuffer, written) = EncodeRequest(request);
            var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);

            Assert.StartsWith($"{methodNames[i]} /api HTTP/1.0", raw);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10RoundTripMethodSpec_should_preservetracemethod()
    {
        var request = new HttpRequestMessage(new HttpMethod("TRACE"), "http://example.com/");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("TRACE / HTTP/1.0", raw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10RoundTripMethodSpec_should_preserveuppercasemethod()
    {
        var request = new HttpRequestMessage(new HttpMethod("CUSTOM"), "http://example.com/");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("CUSTOM / HTTP/1.0", raw);
        Assert.DoesNotContain("custom", raw);
    }
}
