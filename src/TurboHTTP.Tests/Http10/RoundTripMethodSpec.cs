using System.Text;
using Encoder = TurboHTTP.Protocol.Http10.Encoder;

namespace TurboHTTP.Tests.Http10;

public sealed class Http10RoundTripMethodSpec
{
    private static (byte[] Buffer, int Written) EncodeRequest(HttpRequestMessage request)
    {
        var arr = new byte[65536];
        Span<byte> buffer = arr;
        var written = Encoder.Encode(request, ref buffer);
        return (arr[..written], written);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10RoundTripMethodSpec_should_preserve_get_method()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("GET /resource HTTP/1.0", raw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10RoundTripMethodSpec_should_preserve_post_method()
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
    public void Http10RoundTripMethodSpec_should_preserve_put_method()
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
    public void Http10RoundTripMethodSpec_should_preserve_delete_method()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/resource");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("DELETE /resource HTTP/1.0", raw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10RoundTripMethodSpec_should_preserve_patch_method()
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
    public void Http10RoundTripMethodSpec_should_preserve_options_method()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "http://example.com/");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("OPTIONS / HTTP/1.0", raw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10RoundTripMethodSpec_should_preserve_head_method()
    {
        var request = new HttpRequestMessage(HttpMethod.Head, "http://example.com/resource");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("HEAD /resource HTTP/1.0", raw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10RoundTripMethodSpec_should_preserve_query_string()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/search?q=test&page=1");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.Contains("GET /search?q=test&page=1 HTTP/1.0", raw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10RoundTripMethodSpec_should_preserve_post_body()
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
    public void Http10RoundTripMethodSpec_should_preserve_methods_consistently()
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
    public void Http10RoundTripMethodSpec_should_preserve_trace_method()
    {
        var request = new HttpRequestMessage(new HttpMethod("TRACE"), "http://example.com/");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("TRACE / HTTP/1.0", raw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10RoundTripMethodSpec_should_preserve_upper_case_method()
    {
        var request = new HttpRequestMessage(new HttpMethod("CUSTOM"), "http://example.com/");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.StartsWith("CUSTOM / HTTP/1.0", raw);
        Assert.DoesNotContain("custom", raw);
    }
}