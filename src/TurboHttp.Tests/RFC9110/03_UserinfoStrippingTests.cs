using System.Linq;
using System.Net.Http;
using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9110;

/// <summary>
/// Tests for RFC 9110 §4.2.4 — "A sender MUST NOT generate the userinfo subcomponent."
/// Verifies that Http2RequestEncoder strips userinfo from the :authority pseudo-header.
/// </summary>
public sealed class UserinfoStrippingTests
{
    [Fact(DisplayName = "RFC9110-4.2.4-UI-001: H2 authority strips userinfo from http URI")]
    public void H2_Should_StripUserinfo_When_HttpUri()
    {
        var encoder = new Http2RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://user:pass@example.com/path");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var headers = new HpackDecoder().Decode(headerBlock);

        var authority = headers.First(h => h.Name == ":authority").Value;
        Assert.Equal("example.com", authority);
        Assert.DoesNotContain("user", authority);
        Assert.DoesNotContain("@", authority);
    }

    [Fact(DisplayName = "RFC9110-4.2.4-UI-002: H2 authority strips userinfo from https URI")]
    public void H2_Should_StripUserinfo_When_HttpsUri()
    {
        var encoder = new Http2RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://user:pass@secure.example.com/");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var headers = new HpackDecoder().Decode(headerBlock);

        var authority = headers.First(h => h.Name == ":authority").Value;
        Assert.Equal("secure.example.com", authority);
        Assert.DoesNotContain("@", authority);
    }

    [Fact(DisplayName = "RFC9110-4.2.4-UI-003: H2 authority preserves port after stripping")]
    public void H2_Should_PreservePort_When_UserinfoPresent()
    {
        var encoder = new Http2RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://u:p@host.example.com:8080/");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var headers = new HpackDecoder().Decode(headerBlock);

        var authority = headers.First(h => h.Name == ":authority").Value;
        Assert.Equal("host.example.com:8080", authority);
    }

    [Fact(DisplayName = "RFC9110-4.2.4-UI-004: H2 authority unchanged when no userinfo")]
    public void H2_Should_NotChange_When_NoUserinfo()
    {
        var encoder = new Http2RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com:443/resource");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var headers = new HpackDecoder().Decode(headerBlock);

        var authority = headers.First(h => h.Name == ":authority").Value;
        // Port 443 is default for https — should be omitted
        Assert.Equal("example.com", authority);
    }
}
