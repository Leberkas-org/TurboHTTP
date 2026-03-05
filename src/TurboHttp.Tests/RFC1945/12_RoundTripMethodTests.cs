using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC1945;

/// <summary>
/// RFC 1945 Round-Trip Method Tests
/// Verifies that HTTP request methods are preserved through encode→decode cycle
/// </summary>
public sealed class Http10RoundTripMethodTests
{
    private static Memory<byte> MakeBuffer(int size = 8192) => new byte[size];

    private static (byte[] Buffer, int Written) EncodeRequest(HttpRequestMessage request)
    {
        Memory<byte> buffer = new byte[65536];
        var written = Http10Encoder.Encode(request, ref buffer);
        return (buffer.ToArray(), written);
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

    [Fact(DisplayName = "RFC1945-RT-M01: GET method preserved in round-trip")]
    public void Should_PreserveGetMethod_When_RoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("GET /resource HTTP/1.0", raw);
    }

    [Fact(DisplayName = "RFC1945-RT-M02: POST method preserved in round-trip")]
    public void Should_PreservePostMethod_When_RoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Content = new StringContent("data=value")
        };
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("POST /submit HTTP/1.0", raw);
    }

    [Fact(DisplayName = "RFC1945-RT-M03: PUT method preserved in round-trip")]
    public void Should_PreservePutMethod_When_RoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/resource")
        {
            Content = new StringContent("updated content")
        };
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("PUT /resource HTTP/1.0", raw);
    }

    [Fact(DisplayName = "RFC1945-RT-M04: DELETE method preserved in round-trip")]
    public void Should_PreserveDeleteMethod_When_RoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/resource");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("DELETE /resource HTTP/1.0", raw);
    }

    [Fact(DisplayName = "RFC1945-RT-M05: PATCH method preserved in round-trip")]
    public void Should_PreservePatchMethod_When_RoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, "http://example.com/resource")
        {
            Content = new StringContent("{\"op\": \"replace\"}")
        };
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("PATCH /resource HTTP/1.0", raw);
    }

    [Fact(DisplayName = "RFC1945-RT-M06: OPTIONS method preserved in round-trip")]
    public void Should_PreserveOptionsMethod_When_RoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "http://example.com/");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("OPTIONS / HTTP/1.0", raw);
    }

    [Fact(DisplayName = "RFC1945-RT-M07: HEAD method preserved in round-trip")]
    public void Should_PreserveHeadMethod_When_RoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Head, "http://example.com/resource");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("HEAD /resource HTTP/1.0", raw);
    }

    [Fact(DisplayName = "RFC1945-RT-M08: GET with query string round-trip")]
    public void Should_PreserveQueryString_When_GetRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/search?q=test&page=1");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.Contains("GET /search?q=test&page=1 HTTP/1.0", raw);
    }

    [Fact(DisplayName = "RFC1945-RT-M09: POST with body round-trip")]
    public void Should_PreservePostBody_When_PostRoundTrip()
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

    [Fact(DisplayName = "RFC1945-RT-M10: Multiple requests with different methods")]
    public void Should_PreserveMethodsConsistently_When_MultipleRequests()
    {
        var methods = new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete };
        var methodNames = new[] { "GET", "POST", "PUT", "DELETE" };

        for (int i = 0; i < methods.Length; i++)
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

    [Fact(DisplayName = "RFC1945-RT-M11: TRACE method (extension) round-trip")]
    public void Should_PreserveTraceMethod_When_RoundTrip()
    {
        var request = new HttpRequestMessage(new HttpMethod("TRACE"), "http://example.com/");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("TRACE / HTTP/1.0", raw);
    }

    [Fact(DisplayName = "RFC1945-RT-M12: Method case sensitivity (uppercase required)")]
    public void Should_PreserveUppercaseMethod_When_Encoded()
    {
        var request = new HttpRequestMessage(new HttpMethod("CUSTOM"), "http://example.com/");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("CUSTOM / HTTP/1.0", raw);
        Assert.DoesNotContain("custom", raw);
    }
}
