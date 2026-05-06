using System.Buffers;
using System.Text;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Protocol.Semantics;
using Encoder = TurboHTTP.Protocol.Http11.Encoder;

namespace TurboHTTP.Tests.Semantics;

public sealed class UserinfoStrippingSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.2.4")]
    public void H2_should_strip_userinfo_when_http_uri()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://user:pass@example.com/path");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var headers = new HpackDecoder().Decode(headerBlock);

        var authority = headers.First(h => h.Name == ":authority").Value;
        Assert.Equal("example.com", authority);
        Assert.DoesNotContain("user", authority);
        Assert.DoesNotContain("@", authority);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.2.4")]
    public void H2_should_strip_userinfo_when_https_uri()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://user:pass@secure.example.com/");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var headers = new HpackDecoder().Decode(headerBlock);

        var authority = headers.First(h => h.Name == ":authority").Value;
        Assert.Equal("secure.example.com", authority);
        Assert.DoesNotContain("@", authority);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.2.4")]
    public void H2_should_preserve_port_when_userinfo_present()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://u:p@host.example.com:8080/");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var headers = new HpackDecoder().Decode(headerBlock);

        var authority = headers.First(h => h.Name == ":authority").Value;
        Assert.Equal("host.example.com:8080", authority);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.2.4")]
    public void H2_should_not_change_when_no_userinfo()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com:443/resource");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var headers = new HpackDecoder().Decode(headerBlock);

        var authority = headers.First(h => h.Name == ":authority").Value;
        // Port 443 is default for https — should be omitted
        Assert.Equal("example.com", authority);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.2.4")]
    public void H11_should_strip_userinfo_when_absolute_form()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://user:pass@example.com:8080/path?q=1");
        var result = EncodeHttp11Absolute(request);

        Assert.Contains("GET http://example.com:8080/path?q=1 HTTP/1.1\r\n", result);
        Assert.DoesNotContain("user", result);
        Assert.DoesNotContain("pass", result);
        Assert.DoesNotContain("@", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.2.4")]
    public void H11_should_not_contain_userinfo_when_origin_form()
    {
        // Origin-form only emits path+query, so userinfo in the URI never appears
        var request = new HttpRequestMessage(HttpMethod.Get, "http://user:pass@example.com/path?q=1");
        var result = EncodeHttp11Origin(request);

        Assert.Contains("GET /path?q=1 HTTP/1.1\r\n", result);
        Assert.DoesNotContain("user", result);
        Assert.DoesNotContain("@", result);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.2.4")]
    public void Http10_should_strip_userinfo_when_absolute_form()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://user:pass@example.com:8080/path?q=1");
        var result = EncodeHttp10Absolute(request);

        Assert.Contains("GET http://example.com:8080/path?q=1 HTTP/1.0\r\n", result);
        Assert.DoesNotContain("user", result);
        Assert.DoesNotContain("pass", result);
        Assert.DoesNotContain("@", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.2.4")]
    public void Http10_should_not_contain_userinfo_when_origin_form()
    {
        // Origin-form only emits path+query, so userinfo in the URI never appears
        var request = new HttpRequestMessage(HttpMethod.Get, "http://user:pass@example.com/path?q=1");
        var result = EncodeHttp10Origin(request);

        Assert.Contains("GET /path?q=1 HTTP/1.0\r\n", result);
        Assert.DoesNotContain("user", result);
        Assert.DoesNotContain("@", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.2.4")]
    public void Http10_should_not_change_when_no_userinfo()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var result = EncodeHttp10Absolute(request);

        Assert.Contains("GET http://example.com/resource HTTP/1.0\r\n", result);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.2.4")]
    public void FormatAuthority_should_exclude_userinfo()
    {
        var uri = new Uri("http://user:pass@example.com/path");

        var authority = UriSanitizer.FormatAuthority(uri);

        Assert.Equal("example.com", authority);
        Assert.DoesNotContain("user", authority);
        Assert.DoesNotContain("@", authority);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.2.4")]
    public void FormatAuthority_should_include_port()
    {
        var uri = new Uri("http://user:pass@example.com:9090/path");

        var authority = UriSanitizer.FormatAuthority(uri);

        Assert.Equal("example.com:9090", authority);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.2.4")]
    public void FormatAuthority_should_omit_default_port()
    {
        var uriHttp = new Uri("http://example.com:80/path");
        var uriHttps = new Uri("https://example.com:443/path");

        Assert.Equal("example.com", UriSanitizer.FormatAuthority(uriHttp));
        Assert.Equal("example.com", UriSanitizer.FormatAuthority(uriHttps));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.2.4")]
    public void FormatAuthority_should_bracket_ipv6()
    {
        var uri = new Uri("http://[::1]:8080/path");

        var authority = UriSanitizer.FormatAuthority(uri);

        Assert.Equal("[::1]:8080", authority);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.2.4")]
    public void FormatAuthority_should_bracket_ipv6_when_default_port()
    {
        var uri = new Uri("http://[::1]/path");

        var authority = UriSanitizer.FormatAuthority(uri);

        Assert.Equal("[::1]", authority);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.2.4")]
    public void StripUserInfo_should_preserve_path()
    {
        var uri = new Uri("http://user:pass@example.com:8080/path/to/resource?q=1&r=2#section");

        var result = UriSanitizer.StripUserInfo(uri);

        Assert.Contains("http://example.com:8080/path/to/resource", result);
        Assert.Contains("q=1", result);
        Assert.Contains("r=2", result);
        Assert.Contains("#section", result);
        Assert.DoesNotContain("user", result);
        Assert.DoesNotContain("pass", result);
        Assert.DoesNotContain("@", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.2.4")]
    public void StripUserInfo_should_not_change_when_no_userinfo()
    {
        var uri = new Uri("https://example.com/path?q=1#frag");

        var result = UriSanitizer.StripUserInfo(uri);

        Assert.Contains("https://example.com/path", result);
        Assert.Contains("q=1", result);
        Assert.Contains("#frag", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.2.4")]
    public void FormatAbsoluteWithoutUserInfo_should_strip_userinfo_and_fragment()
    {
        var uri = new Uri("http://user:pass@example.com:8080/path?q=1#frag");

        var result = UriSanitizer.FormatAbsoluteWithoutUserInfo(uri);

        Assert.Contains("http://example.com:8080/path", result);
        Assert.Contains("q=1", result);
        Assert.DoesNotContain("user", result);
        Assert.DoesNotContain("#frag", result);
    }


    private static string EncodeHttp10Absolute(HttpRequestMessage request)
    {
        Span<byte> buffer = new byte[4096];
        var written = TurboHTTP.Protocol.Http10.Encoder.Encode(request, ref buffer, absoluteForm: true);
        return Encoding.ASCII.GetString(buffer[..written]);
    }

    private static string EncodeHttp10Origin(HttpRequestMessage request)
    {
        Span<byte> buffer = new byte[4096];
        var written = TurboHTTP.Protocol.Http10.Encoder.Encode(request, ref buffer, absoluteForm: false);
        return Encoding.ASCII.GetString(buffer[..written]);
    }

    private static string EncodeHttp11Absolute(HttpRequestMessage request)
    {
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var span = buffer.Span;
        var written = Encoder.Encode(request, ref span, absoluteForm: true);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }

    private static string EncodeHttp11Origin(HttpRequestMessage request)
    {
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var span = buffer.Span;
        var written = Encoder.Encode(request, ref span, absoluteForm: false);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }
}