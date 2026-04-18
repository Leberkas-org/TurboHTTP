using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.Tests.Http2.Hpack;

public sealed class HpackAppendixCSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-C.2")]
    public void HpackDecoder_should_decode_first_request_when_decoding_appendix_c2_1_without_huffman()
    {
        var encoded = new byte[]
        {
            0x82, // indexed :method: GET (static 2)
            0x86, // indexed :scheme: http (static 6)
            0x84, // indexed :path: / (static 4)
            0x41, 0x0F, // literal incr., nameIdx=1 (:authority), H=0, len=15
            (byte)'w', (byte)'w', (byte)'w', (byte)'.', (byte)'e', (byte)'x', (byte)'a', (byte)'m',
            (byte)'p', (byte)'l', (byte)'e', (byte)'.', (byte)'c', (byte)'o', (byte)'m',
        };
        var decoder = new HpackDecoder();
        var headers = decoder.Decode(encoded);

        Assert.Equal(4, headers.Count);
        Assert.Equal(":method", headers[0].Name);
        Assert.Equal("GET", headers[0].Value);
        Assert.Equal(":scheme", headers[1].Name);
        Assert.Equal("http", headers[1].Value);
        Assert.Equal(":path", headers[2].Name);
        Assert.Equal("/", headers[2].Value);
        Assert.Equal(":authority", headers[3].Name);
        Assert.Equal("www.example.com", headers[3].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-C.2")]
    public void HpackDecoder_should_reference_dynamic_table_when_decoding_appendix_c2_2_second_request()
    {
        var decoder = new HpackDecoder();

        decoder.Decode([
            0x82, 0x86, 0x84,
            0x41, 0x0F,
            (byte)'w', (byte)'w', (byte)'w', (byte)'.', (byte)'e', (byte)'x', (byte)'a', (byte)'m',
            (byte)'p', (byte)'l', (byte)'e', (byte)'.', (byte)'c', (byte)'o', (byte)'m'
        ]);

        var encoded = new byte[]
        {
            0x82, // :method: GET
            0x86, // :scheme: http
            0x84, // :path: /
            0xBE, // indexed dynamic[62] = :authority: www.example.com
            0x58, // literal incr., nameIdx=24 (cache-control)
            0x08, (byte)'n', (byte)'o', (byte)'-', (byte)'c', (byte)'a', (byte)'c', (byte)'h', (byte)'e',
        };
        var headers = decoder.Decode(encoded);

        Assert.Equal(5, headers.Count);
        Assert.Equal(":method", headers[0].Name);
        Assert.Equal("GET", headers[0].Value);
        Assert.Equal(":scheme", headers[1].Name);
        Assert.Equal("http", headers[1].Value);
        Assert.Equal(":path", headers[2].Name);
        Assert.Equal("/", headers[2].Value);
        Assert.Equal(":authority", headers[3].Name);
        Assert.Equal("www.example.com", headers[3].Value);
        Assert.Equal("cache-control", headers[4].Name);
        Assert.Equal("no-cache", headers[4].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-C.2")]
    public void HpackDecoder_should_have_correct_table_state_when_decoding_appendix_c2_3_third_request()
    {
        var decoder = new HpackDecoder();

        decoder.Decode([
            0x82, 0x86, 0x84,
            0x41, 0x0F,
            (byte)'w', (byte)'w', (byte)'w', (byte)'.', (byte)'e', (byte)'x', (byte)'a', (byte)'m',
            (byte)'p', (byte)'l', (byte)'e', (byte)'.', (byte)'c', (byte)'o', (byte)'m'
        ]);

        decoder.Decode([
            0x82, 0x86, 0x84, 0xBE,
            0x58,
            0x08, (byte)'n', (byte)'o', (byte)'-', (byte)'c', (byte)'a', (byte)'c', (byte)'h', (byte)'e'
        ]);

        var encoded = new byte[]
        {
            0x82, // :method: GET
            0x87, // :scheme: https (static 7)
            0x85, // :path: /index.html (static 5)
            0xBF, // indexed dynamic[63] = :authority: www.example.com
            0x40, // literal incr., nameIdx=0 (new name)
            0x0A, // H=0, len=10, "custom-key"
            (byte)'c', (byte)'u', (byte)'s', (byte)'t', (byte)'o', (byte)'m', (byte)'-',
            (byte)'k', (byte)'e', (byte)'y',
            0x0C, // H=0, len=12, "custom-value"
            (byte)'c', (byte)'u', (byte)'s', (byte)'t', (byte)'o', (byte)'m', (byte)'-',
            (byte)'v', (byte)'a', (byte)'l', (byte)'u', (byte)'e',
        };
        var headers = decoder.Decode(encoded);

        Assert.Equal(5, headers.Count);
        Assert.Equal(":method", headers[0].Name);
        Assert.Equal("GET", headers[0].Value);
        Assert.Equal(":scheme", headers[1].Name);
        Assert.Equal("https", headers[1].Value);
        Assert.Equal(":path", headers[2].Name);
        Assert.Equal("/index.html", headers[2].Value);
        Assert.Equal(":authority", headers[3].Name);
        Assert.Equal("www.example.com", headers[3].Value);
        Assert.Equal("custom-key", headers[4].Name);
        Assert.Equal("custom-value", headers[4].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-C.3")]
    public void HpackDecoder_should_decode_all_three_requests_when_decoding_appendix_c3_with_huffman()
    {
        var decoder = new HpackDecoder();

        var req1 = new byte[]
        {
            0x82, 0x86, 0x84,
            0x41, 0x8C,
            0xF1, 0xE3, 0xC2, 0xE5, 0xF2, 0x3A, 0x6B, 0xA0, 0xAB, 0x90, 0xF4, 0xFF,
        };
        var d1 = decoder.Decode(req1);
        Assert.Equal(4, d1.Count);
        Assert.Equal(":authority", d1[3].Name);
        Assert.Equal("www.example.com", d1[3].Value);

        var req2 = new byte[]
        {
            0x82, 0x86, 0x84,
            0xBE, // :authority from dynamic
            0x58, 0x86,
            0xA8, 0xEB, 0x10, 0x64, 0x9C, 0xBF, // "no-cache" Huffman
        };
        var d2 = decoder.Decode(req2);
        Assert.Equal(5, d2.Count);
        Assert.Equal("cache-control", d2[4].Name);
        Assert.Equal("no-cache", d2[4].Value);

        var req3 = new byte[]
        {
            0x82, 0x87, 0x85,
            0xBF, // :authority from [63]
            0x40,
            0x88, 0x25, 0xA8, 0x49, 0xE9, 0x5B, 0xA9, 0x7D, 0x7F, // "custom-key" Huffman
            0x89, 0x25, 0xA8, 0x49, 0xE9, 0x5B, 0xB8, 0xE8, 0xB4, 0xBF, // "custom-value" Huffman
        };
        var d3 = decoder.Decode(req3);
        Assert.Equal(5, d3.Count);
        Assert.Equal(":scheme", d3[1].Name);
        Assert.Equal("https", d3[1].Value);
        Assert.Equal(":path", d3[2].Name);
        Assert.Equal("/index.html", d3[2].Value);
        Assert.Equal(":authority", d3[3].Name);
        Assert.Equal("www.example.com", d3[3].Value);
        Assert.Equal("custom-key", d3[4].Name);
        Assert.Equal("custom-value", d3[4].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-C.4")]
    public void HpackDecoder_should_decode_first_response_when_decoding_appendix_c4_1_without_huffman()
    {
        var encoded = new byte[]
        {
            // :status: 302 — literal incr., nameIdx=8 (:status), value "302"
            0x48, 0x03, (byte)'3', (byte)'0', (byte)'2',
            // cache-control: private — literal incr., nameIdx=24, value "private"
            0x58, 0x07,
            (byte)'p', (byte)'r', (byte)'i', (byte)'v', (byte)'a', (byte)'t', (byte)'e',
            // date: Mon, 21 Oct 2013 20:13:21 GMT — literal incr., nameIdx=33, value 29 chars
            0x61, 0x1D,
            (byte)'M', (byte)'o', (byte)'n', (byte)',', (byte)' ', (byte)'2', (byte)'1', (byte)' ',
            (byte)'O', (byte)'c', (byte)'t', (byte)' ', (byte)'2', (byte)'0', (byte)'1', (byte)'3',
            (byte)' ', (byte)'2', (byte)'0', (byte)':', (byte)'1', (byte)'3', (byte)':', (byte)'2',
            (byte)'1', (byte)' ', (byte)'G', (byte)'M', (byte)'T',
            // location: https://www.example.com — literal incr., nameIdx=46, value 23 chars
            0x6E, 0x17,
            (byte)'h', (byte)'t', (byte)'t', (byte)'p', (byte)'s', (byte)':', (byte)'/', (byte)'/',
            (byte)'w', (byte)'w', (byte)'w', (byte)'.', (byte)'e', (byte)'x', (byte)'a', (byte)'m',
            (byte)'p', (byte)'l', (byte)'e', (byte)'.', (byte)'c', (byte)'o', (byte)'m',
        };

        var decoder = new HpackDecoder();
        var headers = decoder.Decode(encoded);

        Assert.Equal(4, headers.Count);
        Assert.Equal(":status", headers[0].Name);
        Assert.Equal("302", headers[0].Value);
        Assert.Equal("cache-control", headers[1].Name);
        Assert.Equal("private", headers[1].Value);
        Assert.Equal("date", headers[2].Name);
        Assert.Equal("Mon, 21 Oct 2013 20:13:21 GMT", headers[2].Value);
        Assert.Equal("location", headers[3].Name);
        Assert.Equal("https://www.example.com", headers[3].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-C.4")]
    public void HpackDecoder_should_reuse_dynamic_table_when_decoding_appendix_c4_2_second_response()
    {
        var decoder = new HpackDecoder();

        decoder.Decode([
            0x48, 0x03, (byte)'3', (byte)'0', (byte)'2',
            0x58, 0x07, (byte)'p', (byte)'r', (byte)'i', (byte)'v', (byte)'a', (byte)'t', (byte)'e',
            0x61, 0x1D,
            (byte)'M', (byte)'o', (byte)'n', (byte)',', (byte)' ', (byte)'2', (byte)'1', (byte)' ',
            (byte)'O', (byte)'c', (byte)'t', (byte)' ', (byte)'2', (byte)'0', (byte)'1', (byte)'3',
            (byte)' ', (byte)'2', (byte)'0', (byte)':', (byte)'1', (byte)'3', (byte)':', (byte)'2',
            (byte)'1', (byte)' ', (byte)'G', (byte)'M', (byte)'T',
            0x6E, 0x17,
            (byte)'h', (byte)'t', (byte)'t', (byte)'p', (byte)'s', (byte)':', (byte)'/', (byte)'/',
            (byte)'w', (byte)'w', (byte)'w', (byte)'.', (byte)'e', (byte)'x', (byte)'a', (byte)'m',
            (byte)'p', (byte)'l', (byte)'e', (byte)'.', (byte)'c', (byte)'o', (byte)'m'
        ]);

        // After adding :status:307, table is [62]:status:307, [63]:location, [64]:date, [65]:cache-control, [66]:status:302
        var encoded = new byte[]
        {
            0x48, 0x03, (byte)'3', (byte)'0', (byte)'7', // :status: 307 (literal incr.)
            0xC1, // indexed abs[65] = cache-control: private
            0xC0, // indexed abs[64] = date: Mon...
            0xBF, // indexed abs[63] = location: https://...
        };

        var headers = decoder.Decode(encoded);

        Assert.Equal(4, headers.Count);
        Assert.Equal(":status", headers[0].Name);
        Assert.Equal("307", headers[0].Value);
        Assert.Equal("cache-control", headers[1].Name);
        Assert.Equal("private", headers[1].Value);
        Assert.Equal("date", headers[2].Name);
        Assert.Equal("Mon, 21 Oct 2013 20:13:21 GMT", headers[2].Value);
        Assert.Equal("location", headers[3].Name);
        Assert.Equal("https://www.example.com", headers[3].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-C.4")]
    public void HpackDecoder_should_have_correct_table_state_after_c4_2_when_decoding_appendix_c4_3()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var enc41 = encoder.Encode(new List<(string, string)>
        {
            (":status", "302"), ("cache-control", "private"),
            ("date", "Mon, 21 Oct 2013 20:13:21 GMT"), ("location", "https://www.example.com"),
        });
        decoder.Decode(enc41.Span);

        var enc42 = encoder.Encode(new List<(string, string)>
        {
            (":status", "307"), ("cache-control", "private"),
            ("date", "Mon, 21 Oct 2013 20:13:21 GMT"), ("location", "https://www.example.com"),
        });
        decoder.Decode(enc42.Span);

        var c43Headers = new List<(string, string)>
        {
            (":status", "200"), ("cache-control", "private"),
            ("date", "Mon, 21 Oct 2013 20:13:22 GMT"), ("location", "https://www.example.com"),
            ("content-encoding", "gzip"), ("set-cookie", "foo=ASDJKHQKBZXOQWEOPIUAXQWJKHZXCWLKJ"),
        };
        var enc43 = encoder.Encode(c43Headers);
        var headers = decoder.Decode(enc43.Span);

        Assert.Equal(6, headers.Count);
        Assert.Equal(":status", headers[0].Name);
        Assert.Equal("200", headers[0].Value);
        Assert.Equal("cache-control", headers[1].Name);
        Assert.Equal("private", headers[1].Value);
        Assert.Equal("date", headers[2].Name);
        Assert.Equal("Mon, 21 Oct 2013 20:13:22 GMT", headers[2].Value);
        Assert.Equal("location", headers[3].Name);
        Assert.Equal("https://www.example.com", headers[3].Value);
        Assert.Equal("content-encoding", headers[4].Name);
        Assert.Equal("gzip", headers[4].Value);
        Assert.Equal("set-cookie", headers[5].Name);
        Assert.Equal("foo=ASDJKHQKBZXOQWEOPIUAXQWJKHZXCWLKJ", headers[5].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-C.5")]
    public void HpackDecoder_should_decode_correctly_when_decoding_appendix_c5_responses_with_huffman()
    {
        // Use encoder (Huffman) + decoder round-trip to verify the three C.5 responses
        var encoder = new HpackEncoder(useHuffman: true);
        var decoder = new HpackDecoder();

        var responses = new[]
        {
            new List<(string, string)>
            {
                (":status", "302"), ("cache-control", "private"),
                ("date", "Mon, 21 Oct 2013 20:13:21 GMT"), ("location", "https://www.example.com"),
            },
            new List<(string, string)>
            {
                (":status", "307"), ("cache-control", "private"),
                ("date", "Mon, 21 Oct 2013 20:13:21 GMT"), ("location", "https://www.example.com"),
            },
            new List<(string, string)>
            {
                (":status", "200"), ("cache-control", "private"),
                ("date", "Mon, 21 Oct 2013 20:13:22 GMT"), ("location", "https://www.example.com"),
                ("content-encoding", "gzip"),
                ("set-cookie", "foo=ASDJKHQKBZXOQWEOPIUAXQWJKHZXCWLKJ"),
            },
        };

        foreach (var expected in responses)
        {
            var encoded = encoder.Encode(expected);
            var decoded = decoder.Decode(encoded.Span);

            Assert.Equal(expected.Count, decoded.Count);
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].Item1, decoded[i].Name);
                Assert.Equal(expected[i].Item2, decoded[i].Value);
            }
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-C.6")]
    public void HpackDecoder_should_decode_correctly_when_decoding_appendix_c6_large_cookie_responses()
    {
        // C.6 uses three responses, each with large cookie values
        var encoder = new HpackEncoder(useHuffman: true);
        var decoder = new HpackDecoder();

        var responses = new[]
        {
            new List<(string, string)>
            {
                (":status", "200"), ("cache-control", "private"),
                ("date", "Mon, 21 Oct 2013 20:13:21 GMT"), ("location", "https://www.example.com"),
                ("content-encoding", "gzip"),
                ("set-cookie", "foo=ASDJKHQKBZXOQWEOPIUAXQWJKHZXCWLKJ; path=/; Expires=Wed, 09 Jun 2021 10:18:14 GMT"),
            },
            new List<(string, string)>
            {
                (":status", "200"), ("cache-control", "private"),
                ("date", "Mon, 21 Oct 2013 20:13:21 GMT"), ("location", "https://www.example.com"),
                ("content-encoding", "gzip"),
                ("set-cookie", "bar=ASDJKHQKBZXOQWEOPIUAXQWJKHZXCWLKJ; path=/; Expires=Wed, 09 Jun 2021 10:18:14 GMT"),
            },
            new List<(string, string)>
            {
                (":status", "200"), ("cache-control", "private"),
                ("date", "Mon, 21 Oct 2013 20:13:21 GMT"), ("location", "https://www.example.com"),
                ("content-encoding", "gzip"),
                ("set-cookie", "baz=ASDJKHQKBZXOQWEOPIUAXQWJKHZXCWLKJ; path=/; Expires=Wed, 09 Jun 2021 10:18:14 GMT"),
            },
        };

        foreach (var expected in responses)
        {
            var encoded = encoder.Encode(expected);
            var decoded = decoder.Decode(encoded.Span);

            Assert.Equal(expected.Count, decoded.Count);
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].Item1, decoded[i].Name);
                Assert.Equal(expected[i].Item2, decoded[i].Value);
            }
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-C.3")]
    public void HpackDecoder_should_decode_all_three_requests_when_decoding_appendix_c3_with_huffman_encoding()
    {
        // RFC 7541 Appendix C.3 — Request Examples WITH Huffman Coding
        // A single HpackDecoder shares dynamic table state across all three requests,
        // exactly as it would on a persistent HTTP/2 connection.
        var decoder = new HpackDecoder();

        // :method: GET, :scheme: http, :path: /, :authority: www.example.com
        // Dynamic table after: [62] :authority: www.example.com
        var req1 = new byte[]
        {
            0x82, // indexed :method: GET  (static 2)
            0x86, // indexed :scheme: http (static 6)
            0x84, // indexed :path: /      (static 4)
            0x41, // literal incr. indexing, name = static[1] (:authority)
            0x8c, // H=1 (Huffman), length=12
            0xf1, 0xe3, 0xc2, 0xe5, 0xf2, 0x3a, 0x6b, 0xa0, 0xab, 0x90, 0xf4, 0xff, // "www.example.com"
        };

        var d1 = decoder.Decode(req1);
        Assert.Equal(4, d1.Count);
        Assert.Equal(":method", d1[0].Name);
        Assert.Equal("GET", d1[0].Value);
        Assert.Equal(":scheme", d1[1].Name);
        Assert.Equal("http", d1[1].Value);
        Assert.Equal(":path", d1[2].Name);
        Assert.Equal("/", d1[2].Value);
        Assert.Equal(":authority", d1[3].Name);
        Assert.Equal("www.example.com", d1[3].Value);

        // :method: GET, :scheme: http, :path: /, :authority: www.example.com (dynamic),
        // cache-control: no-cache
        // Dynamic table after: [62] cache-control: no-cache, [63] :authority: www.example.com
        var req2 = new byte[]
        {
            0x82, // indexed :method: GET  (static 2)
            0x86, // indexed :scheme: http (static 6)
            0x84, // indexed :path: /      (static 4)
            0xbe, // indexed dynamic[62] → :authority: www.example.com
            0x58, // literal incr. indexing, name = static[24] (cache-control)
            0x86, // H=1, length=6
            0xa8, 0xeb, 0x10, 0x64, 0x9c, 0xbf // "no-cache"
        };

        var d2 = decoder.Decode(req2);
        Assert.Equal(5, d2.Count);
        Assert.Equal(":method", d2[0].Name);
        Assert.Equal("GET", d2[0].Value);
        Assert.Equal(":scheme", d2[1].Name);
        Assert.Equal("http", d2[1].Value);
        Assert.Equal(":path", d2[2].Name);
        Assert.Equal("/", d2[2].Value);
        Assert.Equal(":authority", d2[3].Name);
        Assert.Equal("www.example.com", d2[3].Value);
        Assert.Equal("cache-control", d2[4].Name);
        Assert.Equal("no-cache", d2[4].Value);

        // :method: GET, :scheme: https, :path: /index.html,
        // :authority: www.example.com (dynamic[63]), custom-key: custom-value
        // Dynamic table after: [62] custom-key: custom-value, [63] cache-control: no-cache,
        //                      [64] :authority: www.example.com
        var req3 = new byte[]
        {
            0x82, // :method: GET
            0x87, // :scheme: https (static 7)
            0x85, // :path: /index.html (static 5)
            0xbf, // dynamic[63] → :authority: www.example.com
            0x40, // literal incr. indexing, new literal name
            0x88, // H=1, length=8
            0x25, 0xa8, 0x49, 0xe9, 0x5b, 0xa9, 0x7d, 0x7f, // "custom-key"
            0x89, // H=1, length=9
            0x25, 0xa8, 0x49, 0xe9, 0x5b, 0xb8, 0xe8, 0xb4, 0xbf // "custom-value"
        };

        var d3 = decoder.Decode(req3);
        Assert.Equal(5, d3.Count);
        Assert.Equal(":method", d3[0].Name);
        Assert.Equal("GET", d3[0].Value);
        Assert.Equal(":scheme", d3[1].Name);
        Assert.Equal("https", d3[1].Value);
        Assert.Equal(":path", d3[2].Name);
        Assert.Equal("/index.html", d3[2].Value);
        Assert.Equal(":authority", d3[3].Name);
        Assert.Equal("www.example.com", d3[3].Value);
        Assert.Equal("custom-key", d3[4].Name);
        Assert.Equal("custom-value", d3[4].Value);
    }
}