using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.Hpack;

public sealed class HpackSensitiveHeaderSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeader_should_encode_authorization_as_never_indexed()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer token123");

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.Find(h => h.Name == "authorization");

        Assert.True(header.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeader_should_encode_proxy_authorization_as_never_indexed()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Proxy-Authorization", "Basic dXNlcjpwYXNz");

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.Find(h => h.Name == "proxy-authorization");

        Assert.True(header.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeader_should_encode_cookie_as_never_indexed()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Cookie", "session=abc123; token=xyz");

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.Find(h => h.Name == "cookie");

        Assert.True(header.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeader_should_encode_set_cookie_as_never_indexed()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Set-Cookie", "session=abc; HttpOnly; Secure");

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.Find(h => h.Name == "set-cookie");

        Assert.True(header.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeader_should_detect_authorization_case_insensitive()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("AUTHORIZATION", "Bearer case-test");

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.FirstOrDefault(h => h.Name == "authorization");

        Assert.NotNull(header.Name);
        Assert.True(header.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeader_should_encode_authorization_with_empty_value()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Authorization", "");

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.FirstOrDefault(h => h.Name == "authorization");

        Assert.NotNull(header.Name);
        Assert.True(header.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeader_should_encode_authorization_with_long_value()
    {
        var longToken = $"Bearer {new string('a', 512)}";
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Authorization", longToken);

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.Find(h => h.Name == "authorization");

        Assert.True(header.NeverIndex);
        Assert.Equal(longToken, header.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeader_should_encode_cookie_with_complex_value()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Cookie", "session=abc123; userid=42; pref=dark; lang=en");

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.Find(h => h.Name == "cookie");

        Assert.True(header.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeader_should_not_encode_api_key_as_never_indexed()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("X-Api-Key", "my-api-key");

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.Find(h => h.Name == "x-api-key");

        Assert.False(header.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeader_should_not_encode_user_agent_as_never_indexed()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("User-Agent", "TurboHttp/1.0");

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.Find(h => h.Name == "user-agent");

        Assert.False(header.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeader_should_not_encode_request_id_as_never_indexed()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("X-Request-Id", "req-12345");

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.Find(h => h.Name == "x-request-id");

        Assert.False(header.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeader_should_not_encode_pseudo_headers_as_never_indexed()
    {
        var request = MakeGetRequest();
        var decoded = EncodeAndDecodeHeaders(request);

        foreach (var header in decoded.Where(h => h.Name.StartsWith(':')))
        {
            Assert.False(header.NeverIndex);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeader_should_not_encode_accept_as_never_indexed()
    {
        var request = MakeGetRequest();
        request.Headers.Accept.ParseAdd("application/json");

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.Find(h => h.Name == "accept");

        Assert.False(header.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeader_should_not_add_authorization_to_dynamic_table()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<(string Name, string Value)>
        {
            ("authorization", "Bearer same-token")
        };

        var block1 = encoder.Encode(headers);
        var block2 = encoder.Encode(headers);

        Assert.Equal(block1.Length, block2.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeader_should_reduce_size_for_non_sensitive_on_repeat()
    {
        var encoder = new RequestEncoder(useHuffman: false);

        var req1 = MakeGetRequest();
        req1.Headers.TryAddWithoutValidation("X-Custom-Header", "some-stable-value");

        var req2 = MakeGetRequest();
        req2.Headers.TryAddWithoutValidation("X-Custom-Header", "some-stable-value");

        var block1 = ExtractHpackBlockFromEncoder(encoder, req1);
        var block2 = ExtractHpackBlockFromEncoder(encoder, req2);

        Assert.True(block2.Length < block1.Length,
            "Second encoding of non-sensitive header should be smaller due to HPACK dynamic table caching");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeader_should_never_use_indexed_reference_for_authorization()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<(string Name, string Value)>
        {
            ("authorization", "Bearer repeated-token")
        };

        var size1 = encoder.Encode(headers).Length;
        var size2 = encoder.Encode(headers).Length;
        var size3 = encoder.Encode(headers).Length;

        Assert.Equal(size1, size2);
        Assert.Equal(size2, size3);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeader_should_never_use_indexed_reference_for_cookie()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<(string Name, string Value)>
        {
            ("cookie", "session=stable-value")
        };

        var size1 = encoder.Encode(headers).Length;
        var size2 = encoder.Encode(headers).Length;
        var size3 = encoder.Encode(headers).Length;

        Assert.Equal(size1, size2);
        Assert.Equal(size2, size3);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeader_should_preserve_authorization_value_round_trip()
    {
        const string token = "Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.payload.signature";
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Authorization", token);

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.Find(h => h.Name == "authorization");

        Assert.Equal(token, header.Value);
        Assert.True(header.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeader_should_preserve_proxy_authorization_value_round_trip()
    {
        const string value = "Basic dXNlcjpwYXNzd29yZA==";
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Proxy-Authorization", value);

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.Find(h => h.Name == "proxy-authorization");

        Assert.Equal(value, header.Value);
        Assert.True(header.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeader_should_preserve_cookie_value_round_trip()
    {
        const string value = "session=abc123; userId=42; csrfToken=xyz789";
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Cookie", value);

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.Find(h => h.Name == "cookie");

        Assert.Equal(value, header.Value);
        Assert.True(header.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeader_should_preserve_set_cookie_value_round_trip()
    {
        const string value = "sessionId=38afes71g; HttpOnly; Secure; SameSite=Strict";
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Set-Cookie", value);

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.Find(h => h.Name == "set-cookie");

        Assert.Equal(value, header.Value);
        Assert.True(header.NeverIndex);
    }

    private static HttpRequestMessage MakeGetRequest(string url = "https://api.example.com/v1/resource")
        => new(HttpMethod.Get, url);

    private static List<HpackHeader> EncodeAndDecodeHeaders(HttpRequestMessage request, bool useHuffman = false)
    {
        var encoder = new RequestEncoder(useHuffman);
        var hpackBlock = encoder.EncodeToHpackBlock(request);
        return new HpackDecoder().Decode(hpackBlock);
    }

    private static byte[] ExtractHpackBlockFromEncoder(RequestEncoder encoder, HttpRequestMessage request)
    {
        return encoder.EncodeToHpackBlock(request);
    }
}
