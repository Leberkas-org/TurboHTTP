using System.Buffers;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9112;

public sealed class Http11EncoderHeaderTests
{
    [Fact(DisplayName = "RFC7230-3.2: Header field format is Name: SP value CRLF")]
    public void Test_Header_Format()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "X-Custom", "test-value" } }
        };
        var result = Encode(request);
        Assert.Contains("X-Custom: test-value\r\n", result);
    }

    [Fact(DisplayName = "RFC7230-3.2: No spurious whitespace added to header values")]
    public void Test_No_Spurious_Whitespace()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "X-Test", "value" } }
        };
        var result = Encode(request);
        Assert.Contains("X-Test: value\r\n", result);
        Assert.DoesNotContain("X-Test:  value", result);
    }

    [Fact(DisplayName = "RFC7230-3.2: Header name casing preserved in output")]
    public void Test_Header_Name_Casing_Preserved()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("X-Custom-Header", "value");
        var result = Encode(request);
        Assert.Contains("X-Custom-Header: value\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-3.2: NUL byte in header value throws exception")]
    public void Test_NUL_Byte_Rejected()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("X-Bad", "value\0bad");
        var buffer = new Memory<byte>(new byte[4096]);

        Assert.Throws<ArgumentException>(() =>
        {
            var span = buffer.Span;
            Http11Encoder.Encode(request, ref span);
        });
    }

    [Fact(DisplayName = "RFC9112-3.2: Content-Type with charset parameter preserved")]
    public void Test_Content_Type_With_Charset()
    {
        var content = new StringContent("test", Encoding.UTF8, "text/html");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/")
        {
            Content = content
        };
        var result = Encode(request);
        Assert.Contains("Content-Type: text/html; charset=utf-8\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-3.2: All custom headers appear in output")]
    public void Test_Custom_Headers_Appear()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers =
            {
                { "X-First", "value1" },
                { "X-Second", "value2" },
                { "X-Third", "value3" }
            }
        };
        var result = Encode(request);
        Assert.Contains("X-First: value1\r\n", result);
        Assert.Contains("X-Second: value2\r\n", result);
        Assert.Contains("X-Third: value3\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-3.2: Accept-Encoding gzip,deflate encoded")]
    public void Test_Accept_Encoding()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
        var result = Encode(request);
        Assert.Contains("Accept-Encoding: gzip, deflate\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-3.2: Authorization header preserved verbatim")]
    public void Test_Authorization_Preserved()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "Authorization", "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9" } }
        };
        var result = Encode(request);
        Assert.Contains("Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9\r\n", result);
    }

    [Fact]
    public void BearerToken_SetsAuthorizationHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/protected")
        {
            Headers = { { "Authorization", "Bearer my-secret-token" } }
        };
        var result = Encode(request);
        Assert.Contains("Authorization: Bearer my-secret-token\r\n", result);
    }

    private static string Encode(HttpRequestMessage request)
    {
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var span = buffer.Span;
        var written = Http11Encoder.Encode(request, ref span);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }
}