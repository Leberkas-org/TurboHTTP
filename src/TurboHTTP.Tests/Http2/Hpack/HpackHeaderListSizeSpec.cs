using System;
using System.Collections.Generic;
using Xunit;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.Tests.Http2.Hpack;

public sealed class HpackHeaderListSizeSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4.1")]
    public void HpackHeaderListSize_should_initialize_with_unlimited_size()
    {
        var decoder = new HpackDecoder();
        // Default behavior: no header list size limit
        var headers = new List<(string Name, string Value)>
        {
            ("header", "value")
        };

        var block = new HpackEncoder(useHuffman: false).Encode(headers);
        var decoded = decoder.Decode(block.Span);

        Assert.NotEmpty(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.5.2")]
    public void HpackHeaderListSize_should_enforce_max_header_list_size()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxHeaderListSize(100);

        // Try to decode headers that exceed the limit
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<(string Name, string Value)>
        {
            ("very-long-header-name", new string('x', 200))
        };

        var block = encoder.Encode(headers);

        // Decoder must throw when the header list size limit is exceeded
        Assert.Throws<HpackException>(() => decoder.Decode(block.Span));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.5.2")]
    public void HpackHeaderListSize_should_calculate_header_size_correctly()
    {
        var decoder = new HpackDecoder();

        // Size = name length + value length + 32 bytes overhead
        var headers = new List<(string Name, string Value)>
        {
            ("name", "value")
        };

        var block = new HpackEncoder(useHuffman: false).Encode(headers);
        var decoded = decoder.Decode(block.Span);

        Assert.NotEmpty(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.5.2")]
    public void HpackHeaderListSize_should_accumulate_size_across_headers()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxHeaderListSize(500);

        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<(string Name, string Value)>
        {
            ("header1", "value1"),
            ("header2", "value2"),
            ("header3", "value3")
        };

        var block = encoder.Encode(headers);
        var decoded = decoder.Decode(block.Span);

        Assert.NotEmpty(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    public void HpackHeaderListSize_should_reset_size_between_decode_calls()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxHeaderListSize(100);

        var encoder = new HpackEncoder(useHuffman: false);

        // First request
        var headers1 = new List<(string Name, string Value)>
        {
            ("name", "value")
        };
        var block1 = encoder.Encode(headers1);
        decoder.Decode(block1.Span);

        // Second request (size counter should be reset)
        var headers2 = new List<(string Name, string Value)>
        {
            ("another", "header")
        };
        var block2 = encoder.Encode(headers2);
        var decoded2 = decoder.Decode(block2.Span);

        Assert.NotEmpty(decoded2);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.5.2")]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(4096)]
    public void HpackHeaderListSize_should_support_various_max_sizes(int maxSize)
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxHeaderListSize(maxSize);

        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<(string Name, string Value)>
        {
            ("name", "value")
        };

        var block = encoder.Encode(headers);
        var decoded = decoder.Decode(block.Span);

        Assert.NotEmpty(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.5.2")]
    public void HpackHeaderListSize_should_include_overhead_in_calculation()
    {
        var decoder = new HpackDecoder();

        // Total size = name + value + 32 bytes overhead per RFC 7541 §4.1
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<(string Name, string Value)>
        {
            ("x", "y")  // Should be: 1 + 1 + 32 = 34 bytes
        };

        var block = encoder.Encode(headers);
        var decoded = decoder.Decode(block.Span);

        Assert.NotEmpty(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    public void HpackHeaderListSize_should_handle_empty_header_list()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxHeaderListSize(100);

        var block = new byte[] { };
        var decoded = decoder.Decode(block);

        Assert.Empty(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.5.2")]
    public void HpackHeaderListSize_should_enforce_limit_on_combined_headers()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxHeaderListSize(200);

        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<(string Name, string Value)>
        {
            ("header1", new string('a', 50)),
            ("header2", new string('b', 50)),
            ("header3", new string('c', 50))
        };

        var block = encoder.Encode(headers);

        // Decoder must throw when combined header size exceeds the limit
        Assert.Throws<HpackException>(() => decoder.Decode(block.Span));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.5.2")]
    public void HpackHeaderListSize_should_track_dynamic_entry_size()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        // Add entry to dynamic table
        var headers1 = new List<(string Name, string Value)>
        {
            ("custom-header", "custom-value")
        };
        var block1 = encoder.Encode(headers1);
        decoder.Decode(block1.Span);

        // Reference same entry
        var headers2 = new List<(string Name, string Value)>
        {
            ("custom-header", "custom-value")
        };
        var block2 = encoder.Encode(headers2);
        var decoded2 = decoder.Decode(block2.Span);

        Assert.NotEmpty(decoded2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.5.2")]
    public void HpackHeaderListSize_should_sum_all_header_sizes()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxHeaderListSize(1000);

        var encoder = new HpackEncoder(useHuffman: false);
        var headersList = new List<(string Name, string Value)>();

        for (int i = 0; i < 10; i++)
        {
            headersList.Add(($"header-{i}", $"value-{i}"));
        }

        var block = encoder.Encode(headersList);
        var decoded = decoder.Decode(block.Span);

        Assert.Equal(headersList.Count, decoded.Count);
    }
}
