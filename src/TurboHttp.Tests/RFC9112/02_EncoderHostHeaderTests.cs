using System.Buffers;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9112;

public sealed class Http11EncoderHostHeaderTests
{
    [Fact(DisplayName = "RFC9112-5.4: Host header mandatory in HTTP/1.1")]
    public void Test_Host_Always_Present()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.Contains("Host: example.com\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-5.4: Host header emitted exactly once")]
    public void Test_Host_Emitted_Once()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        var count = System.Text.RegularExpressions.Regex.Matches(result, "Host:").Count;
        Assert.Equal(1, count);
    }

    [Fact(DisplayName = "RFC9112-5.4: Host with non-standard port includes port")]
    public void Test_Non_Standard_Port()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/");
        var result = Encode(request);
        Assert.Contains("Host: example.com:8080\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-5.4: IPv6 host literal bracketed correctly")]
    public void Test_IPv6_Bracketed()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://[::1]:8080/");
        var result = Encode(request);
        Assert.Contains("Host: [::1]:8080\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-5.4: Default port 80 omitted from Host header")]
    public void Test_Default_Port_Omitted()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:80/");
        var result = Encode(request);
        Assert.Contains("Host: example.com\r\n", result);
        Assert.DoesNotContain("Host: example.com:80", result);
    }

    [Fact]
    public void Get_ContainsHostHeader_Port80_NoPort()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:80/");
        var result = Encode(request);
        Assert.Contains("Host: example.com\r\n", result);
    }

    [Fact]
    public void Get_ContainsHostHeader_Port443_NoPort()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure");
        var result = Encode(request);
        Assert.Contains("Host: example.com\r\n", result);
    }

    [Fact]
    public void Get_NonStandardPort_IncludesPortInHost()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/");
        var result = Encode(request);
        Assert.Contains("Host: example.com:8080\r\n", result);
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