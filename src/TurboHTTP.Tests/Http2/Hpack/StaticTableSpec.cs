using System.Collections.Generic;
using Xunit;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.Tests.Http2.Hpack;

/// <summary>
/// Tests the HPACK static table definition per RFC 7541 Appendix A.
/// Verifies table size, reserved index 0, and correctness of all 61 static entries.
/// </summary>
public sealed class StaticTableSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackStaticTable_should_have_exactly_61_entries()
    {
        Assert.Equal(61, HpackStaticTable.StaticCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackStaticTable_should_have_62_slots_with_reserved_index_zero()
    {
        Assert.Equal(62, HpackStaticTable.Entries.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackStaticTable_should_reserve_index_zero_with_empty_name_and_value()
    {
        var entry = HpackStaticTable.Entries[0];
        Assert.Equal(string.Empty, entry.Name);
        Assert.Equal(string.Empty, entry.Value);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    [MemberData(nameof(AllStaticEntries))]
    public void HpackStaticTable_should_have_correct_name_and_value_at_index(int index, string expectedName, string expectedValue)
    {
        var entry = HpackStaticTable.Entries[index];
        Assert.Equal(expectedName, entry.Name);
        Assert.Equal(expectedValue, entry.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackDecoder_should_return_method_get_when_decoding_index_2()
    {
        var decoder = new HpackDecoder();
        var result = decoder.Decode([0x82]);
        Assert.Single(result);
        Assert.Equal(":method", result[0].Name);
        Assert.Equal("GET", result[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackDecoder_should_return_method_post_when_decoding_index_3()
    {
        var decoder = new HpackDecoder();
        var result = decoder.Decode([0x83]);
        Assert.Single(result);
        Assert.Equal(":method", result[0].Name);
        Assert.Equal("POST", result[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackDecoder_should_return_path_root_when_decoding_index_4()
    {
        var decoder = new HpackDecoder();
        var result = decoder.Decode([0x84]);
        Assert.Single(result);
        Assert.Equal(":path", result[0].Name);
        Assert.Equal("/", result[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackDecoder_should_return_path_index_html_when_decoding_index_5()
    {
        var decoder = new HpackDecoder();
        var result = decoder.Decode([0x85]);
        Assert.Single(result);
        Assert.Equal(":path", result[0].Name);
        Assert.Equal("/index.html", result[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackDecoder_should_return_scheme_https_when_decoding_index_7()
    {
        var decoder = new HpackDecoder();
        var result = decoder.Decode([0x87]);
        Assert.Single(result);
        Assert.Equal(":scheme", result[0].Name);
        Assert.Equal("https", result[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackDecoder_should_return_status_200_when_decoding_index_8()
    {
        var decoder = new HpackDecoder();
        var result = decoder.Decode([0x88]);
        Assert.Single(result);
        Assert.Equal(":status", result[0].Name);
        Assert.Equal("200", result[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackDecoder_should_return_status_404_when_decoding_index_13()
    {
        var decoder = new HpackDecoder();
        var result = decoder.Decode([0x8D]);
        Assert.Single(result);
        Assert.Equal(":status", result[0].Name);
        Assert.Equal("404", result[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackDecoder_should_return_accept_encoding_when_decoding_index_16()
    {
        var decoder = new HpackDecoder();
        var result = decoder.Decode([0x90]);
        Assert.Single(result);
        Assert.Equal("accept-encoding", result[0].Name);
        Assert.Equal("gzip, deflate", result[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackDecoder_should_return_www_authenticate_when_decoding_index_61()
    {
        var decoder = new HpackDecoder();
        var result = decoder.Decode([0xBD]);
        Assert.Single(result);
        Assert.Equal("www-authenticate", result[0].Name);
        Assert.Equal("", result[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackEncoder_should_produce_byte_0x82_when_encoding_method_get()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":method", "GET")]);

        Assert.Equal(1, encoded.Length);
        Assert.Equal(0x82, encoded.Span[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackEncoder_should_produce_byte_0x83_when_encoding_method_post()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":method", "POST")]);

        Assert.Equal(1, encoded.Length);
        Assert.Equal(0x83, encoded.Span[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackEncoder_should_produce_byte_0x84_when_encoding_path_root()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":path", "/")]);

        Assert.Equal(1, encoded.Length);
        Assert.Equal(0x84, encoded.Span[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackEncoder_should_produce_byte_0x87_when_encoding_scheme_https()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":scheme", "https")]);

        Assert.Equal(1, encoded.Length);
        Assert.Equal(0x87, encoded.Span[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackEncoder_should_produce_byte_0x88_when_encoding_status_200()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "200")]);

        Assert.Equal(1, encoded.Length);
        Assert.Equal(0x88, encoded.Span[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackEncoder_should_produce_byte_0x8D_when_encoding_status_404()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "404")]);

        Assert.Equal(1, encoded.Length);
        Assert.Equal(0x8D, encoded.Span[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackDecoder_should_throw_hpackexception_when_decoding_index_zero()
    {
        var decoder = new HpackDecoder();
        var ex = Assert.Throws<HpackException>(() => decoder.Decode([0x80]));
        Assert.Contains("0", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackDecoder_should_throw_hpackexception_when_decoding_index_62_with_empty_dynamic_table()
    {
        var decoder = new HpackDecoder();
        var ex = Assert.Throws<HpackException>(() => decoder.Decode([0xBE]));
        Assert.Contains("62", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackDecoder_should_throw_hpackexception_when_decoding_index_100_with_empty_dynamic_table()
    {
        var decoder = new HpackDecoder();
        var ex = Assert.Throws<HpackException>(() => decoder.Decode([0xE4]));
        Assert.Contains("100", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackDecoder_should_throw_hpackexception_when_decoding_very_large_index()
    {
        var decoder = new HpackDecoder();
        byte[] bytes = [0xFF, 0xE8, 0x06];
        var ex = Assert.Throws<HpackException>(() => decoder.Decode(bytes));
        Assert.Contains("999", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackEncoder_should_use_static_name_index_1_when_encoding_authority_with_custom_value()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":authority", "example.com")]);

        Assert.True(encoded.Length > 1, "Should be more than 1 byte (name index + value)");
        Assert.Equal(0x41, encoded.Span[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackEncoder_should_use_static_name_index_16_when_encoding_accept_encoding_with_custom_value()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(("accept-encoding", "br"))]);

        Assert.True(encoded.Length > 1);
        Assert.Equal(0x50, encoded.Span[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackEncoder_should_encode_and_decode_correctly_when_round_tripping_all_pseudo_headers_via_static_table()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":method", "POST"),
            (":path", "/index.html"),
            (":scheme", "http"),
            (":status", "200"),
            (":status", "204"),
            (":status", "304"),
            (":status", "404"),
            (":status", "500"),
        };

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Equal(headers.Count, decoded.Count);
        for (var i = 0; i < headers.Count; i++)
        {
            Assert.Equal(headers[i].Item1, decoded[i].Name);
            Assert.Equal(headers[i].Item2, decoded[i].Value);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackEncoder_should_produce_single_byte_when_encoding_all_static_full_match_entries()
    {
        var fullMatchEntries = new List<(string, string)>
        {
            (":method", "GET"),
            (":method", "POST"),
            (":path", "/"),
            (":path", "/index.html"),
            (":scheme", "http"),
            (":scheme", "https"),
            (":status", "200"),
            (":status", "204"),
            (":status", "206"),
            (":status", "304"),
            (":status", "400"),
            (":status", "404"),
            (":status", "500"),
            ("accept-encoding", "gzip, deflate"),
        };

        foreach (var (name, value) in fullMatchEntries)
        {
            var encoder = new HpackEncoder(useHuffman: false);
            var encoded = encoder.Encode([(name, value)]);
            Assert.True(encoded.Length == 1, $"Expected 1 byte for {name}={value}, got {encoded.Length}");
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackDecoder_should_resolve_correctly_when_decoding_all_static_indices()
    {
        var decoder = new HpackDecoder();

        for (var idx = 1; idx <= HpackStaticTable.StaticCount; idx++)
        {
            var expected = HpackStaticTable.Entries[idx];
            byte[] bytes = [(byte)(0x80 | idx)];

            var result = decoder.Decode(bytes);
            Assert.True(result.Count == 1, $"Expected 1 decoded header for static index {idx}, got {result.Count}");
            Assert.True(expected.Name == result[0].Name, $"Name mismatch at index {idx}: expected '{expected.Name}', got '{result[0].Name}'");
            Assert.True(expected.Value == result[0].Value, $"Value mismatch at index {idx}: expected '{expected.Value}', got '{result[0].Value}'");
        }
    }

    public static TheoryData<int, string, string> AllStaticEntries()
    {
        return new TheoryData<int, string, string>
        {
            { 1,  ":authority",                  "" },
            { 2,  ":method",                     "GET" },
            { 3,  ":method",                     "POST" },
            { 4,  ":path",                       "/" },
            { 5,  ":path",                       "/index.html" },
            { 6,  ":scheme",                     "http" },
            { 7,  ":scheme",                     "https" },
            { 8,  ":status",                     "200" },
            { 9,  ":status",                     "204" },
            { 10, ":status",                     "206" },
            { 11, ":status",                     "304" },
            { 12, ":status",                     "400" },
            { 13, ":status",                     "404" },
            { 14, ":status",                     "500" },
            { 15, "accept-charset",              "" },
            { 16, "accept-encoding",             "gzip, deflate" },
            { 17, "accept-language",             "" },
            { 18, "accept-ranges",               "" },
            { 19, "accept",                      "" },
            { 20, "access-control-allow-origin", "" },
            { 21, "age",                         "" },
            { 22, "allow",                       "" },
            { 23, "authorization",               "" },
            { 24, "cache-control",               "" },
            { 25, "content-disposition",         "" },
            { 26, "content-encoding",            "" },
            { 27, "content-language",            "" },
            { 28, "content-length",              "" },
            { 29, "content-location",            "" },
            { 30, "content-range",               "" },
            { 31, "content-type",                "" },
            { 32, "cookie",                      "" },
            { 33, "date",                        "" },
            { 34, "etag",                        "" },
            { 35, "expect",                      "" },
            { 36, "expires",                     "" },
            { 37, "from",                        "" },
            { 38, "host",                        "" },
            { 39, "if-match",                    "" },
            { 40, "if-modified-since",           "" },
            { 41, "if-none-match",               "" },
            { 42, "if-range",                    "" },
            { 43, "if-unmodified-since",         "" },
            { 44, "last-modified",               "" },
            { 45, "link",                        "" },
            { 46, "location",                    "" },
            { 47, "max-forwards",                "" },
            { 48, "proxy-authenticate",          "" },
            { 49, "proxy-authorization",         "" },
            { 50, "range",                       "" },
            { 51, "referer",                     "" },
            { 52, "refresh",                     "" },
            { 53, "retry-after",                 "" },
            { 54, "server",                      "" },
            { 55, "set-cookie",                  "" },
            { 56, "strict-transport-security",   "" },
            { 57, "transfer-encoding",           "" },
            { 58, "user-agent",                  "" },
            { 59, "vary",                        "" },
            { 60, "via",                         "" },
            { 61, "www-authenticate",            "" },
        };
    }
}
