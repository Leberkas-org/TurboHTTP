using System.Buffers;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9112;

public sealed class Http11EncoderConnectionTests
{
    [Fact(DisplayName = "RFC7230-6.1: Connection keep-alive default in HTTP/1.1")]
    public void Test_Default_Keep_Alive()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.Contains("Connection: keep-alive\r\n", result);
    }

    [Fact(DisplayName = "RFC7230-6.1: Connection close encoded when set")]
    public void Test_Connection_Close()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "Connection", "close" } }
        };
        var result = Encode(request);
        Assert.Contains("Connection: close\r\n", result);
        Assert.DoesNotContain("keep-alive", result);
    }

    [Fact(DisplayName = "RFC7230-6.1: Multiple Connection tokens encoded")]
    public void Test_Multiple_Connection_Tokens()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.Connection.Add("upgrade");
        var result = Encode(request);
        Assert.Contains("Connection: upgrade, keep-alive\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-6.1: Connection-specific headers stripped")]
    public void Test_Connection_Specific_Headers_Stripped()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("TE", "trailers");
        request.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5");
        request.Headers.TryAddWithoutValidation("Upgrade", "websocket");
        var result = Encode(request);
        Assert.DoesNotContain("TE:", result);
        Assert.DoesNotContain("Keep-Alive:", result);
        Assert.DoesNotContain("Upgrade:", result);
    }

    [Fact]
    public void Get_DefaultConnectionHeader_IsKeepAlive()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.Contains("Connection: keep-alive\r\n", result);
    }

    [Fact]
    public void Get_ExplicitConnectionClose_IsPreserved()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "Connection", "close" } }
        };
        var result = Encode(request);
        Assert.Contains("Connection: close\r\n", result);
        Assert.DoesNotContain("Connection: keep-alive", result);
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
