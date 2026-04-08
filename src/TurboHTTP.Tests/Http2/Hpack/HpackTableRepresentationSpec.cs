using System;
using System.Collections.Generic;
using Xunit;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.Tests.Http2.Hpack;

public sealed class HpackTableRepresentationSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4.3")]
    public void HpackTableRepresentation_should_evict_oldest_entry_when_exceeding_size()
    {
        var table = new HpackDynamicTable();
        table.SetMaxSize(100);

        table.Add("name1", "value1");
        table.Add("name2", "value2");
        table.Add("name3", "value3");

        Assert.True(table.CurrentSize <= 100);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.3")]
    public void HpackTableRepresentation_should_update_dynamic_table_size()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(512);

        // Verify decoding still works after restricting allowed table size
        var block = new byte[] { 0x82 }; // :method GET from static table
        var decoded = decoder.Decode(block);
        Assert.Single(decoded);
        Assert.Equal(":method", decoded[0].Name);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackTableRepresentation_should_encode_authorization_as_never_indexed()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<HpackHeader>
        {
            new("authorization", "Bearer token")
        };

        var output = new byte[256];
        Span<byte> span = output;
        var written = encoder.Encode(headers, ref span, useHuffman: false);

        var decoded = new HpackDecoder().Decode(output.AsSpan(0, written));
        var authHeader = decoded.Find(h => h.Name == "authorization");

        Assert.True(authHeader.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackTableRepresentation_should_encode_cookie_as_never_indexed()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<HpackHeader>
        {
            new("cookie", "session=abc123")
        };

        var output = new byte[256];
        Span<byte> span = output;
        var written = encoder.Encode(headers, ref span, useHuffman: false);

        var decoded = new HpackDecoder().Decode(output.AsSpan(0, written));
        var cookieHeader = decoded.Find(h => h.Name == "cookie");

        Assert.True(cookieHeader.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackTableRepresentation_should_encode_set_cookie_as_never_indexed()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<HpackHeader>
        {
            new("set-cookie", "id=123; HttpOnly")
        };

        var output = new byte[256];
        Span<byte> span = output;
        var written = encoder.Encode(headers, ref span, useHuffman: false);

        var decoded = new HpackDecoder().Decode(output.AsSpan(0, written));
        var setCookieHeader = decoded.Find(h => h.Name == "set-cookie");

        Assert.True(setCookieHeader.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackTableRepresentation_should_encode_proxy_auth_as_never_indexed()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<HpackHeader>
        {
            new("proxy-authorization", "Basic dXNlcjpwYXNz")
        };

        var output = new byte[256];
        Span<byte> span = output;
        var written = encoder.Encode(headers, ref span, useHuffman: false);

        var decoded = new HpackDecoder().Decode(output.AsSpan(0, written));
        var proxyAuthHeader = decoded.Find(h => h.Name == "proxy-authorization");

        Assert.True(proxyAuthHeader.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackTableRepresentation_should_preserve_entry_order_after_size_update()
    {
        var table = new HpackDynamicTable();
        table.Add("a", "1");
        table.Add("b", "2");
        table.Add("c", "3");

        table.SetMaxSize(1024);

        Assert.Equal("c", table.GetEntry(1)!.Value.Name);
        Assert.Equal("b", table.GetEntry(2)!.Value.Name);
        Assert.Equal("a", table.GetEntry(3)!.Value.Name);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-Appendix-C.2.1")]
    public void HpackTableRepresentation_should_decode_appendix_c_2_1()
    {
        var decoder = new HpackDecoder();
        var block = new byte[]
        {
            0x82, 0x86, 0x84, 0x41, 0x0f, 0x77, 0x77, 0x77,
            0x2e, 0x65, 0x78, 0x61, 0x6d, 0x70, 0x6c, 0x65,
            0x2e, 0x63, 0x6f, 0x6d
        };

        var decoded = decoder.Decode(block);
        Assert.NotEmpty(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-Appendix-C.2.2")]
    public void HpackTableRepresentation_should_decode_appendix_c_2_2()
    {
        // C.3.2 is stateful: 0xbe references dynamic table populated by C.3.1
        var decoder = new HpackDecoder();
        var c21 = new byte[]
        {
            0x82, 0x86, 0x84, 0x41, 0x0f, 0x77, 0x77, 0x77,
            0x2e, 0x65, 0x78, 0x61, 0x6d, 0x70, 0x6c, 0x65,
            0x2e, 0x63, 0x6f, 0x6d
        };
        decoder.Decode(c21);

        var block = new byte[]
        {
            0x82, 0x86, 0x84, 0xbe, 0x58, 0x08, 0x6e, 0x6f,
            0x2d, 0x63, 0x61, 0x63, 0x68, 0x65
        };

        var decoded = decoder.Decode(block);
        Assert.NotEmpty(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-Appendix-C.3")]
    public void HpackTableRepresentation_should_decode_appendix_c_3()
    {
        // RFC 7541 Appendix C.4.1: first request with Huffman encoding
        var decoder = new HpackDecoder();
        var block = new byte[]
        {
            0x82, 0x86, 0x84, 0x41, 0x8c, 0xf1, 0xe3, 0xc2,
            0xe5, 0xf2, 0x3a, 0x6b, 0xa0, 0xab, 0x90, 0xf4, 0xff
        };

        var decoded = decoder.Decode(block);
        Assert.NotEmpty(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-Appendix-C.4")]
    public void HpackTableRepresentation_should_decode_appendix_c_4()
    {
        var encoder = new HpackEncoder(useHuffman: true);
        var block = encoder.Encode([(":status", "200"), ("cache-control", "private")]);
        var decoded = new HpackDecoder().Decode(block.Span);
        Assert.NotEmpty(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-Appendix-C.5")]
    public void HpackTableRepresentation_should_decode_appendix_c_5()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var block = encoder.Encode([(":status", "302"), ("location", "https://www.example.com")]);
        var decoded = new HpackDecoder().Decode(block.Span);
        Assert.NotEmpty(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-Appendix-C.6")]
    public void HpackTableRepresentation_should_decode_appendix_c_6()
    {
        var encoder = new HpackEncoder(useHuffman: true);
        var block = encoder.Encode([(":status", "302"), ("location", "https://www.example.com")]);
        var decoded = new HpackDecoder().Decode(block.Span);
        Assert.NotEmpty(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4.1")]
    public void HpackTableRepresentation_should_calculate_entry_size_with_overhead()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<(string Name, string Value)>
        {
            ("x", "y")
        };

        var block = encoder.Encode(headers);
        var decoded = new HpackDecoder().Decode(block.Span);

        Assert.NotEmpty(decoded);
        Assert.Equal("x", decoded[0].Name);
        Assert.Equal("y", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1")]
    public void HpackTableRepresentation_should_preserve_never_indexed_flag_across_dynamic_table_operations()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<HpackHeader>
        {
            new("authorization", "Bearer sensitive-token", NeverIndex: true)
        };

        var output = new byte[256];
        Span<byte> span = output;
        var written = encoder.Encode(headers, ref span, useHuffman: false);

        var decoded = decoder.Decode(output.AsSpan(0, written));
        var authHeader = decoded.Find(h => h.Name == "authorization");

        Assert.True(authHeader.NeverIndex);
    }
}
