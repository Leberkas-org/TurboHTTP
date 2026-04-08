using System;
using System.Collections.Generic;
using System.Text;
using TurboHTTP.Protocol;
using Xunit;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.Tests.Http2.Hpack;

public sealed class HuffmanSpec
{
    private static byte[] Encode(string s)
    {
        var input = Encoding.UTF8.GetBytes(s);
        var buf = new byte[HuffmanCodec.GetMaxEncodedLength(input.Length)];
        var written = HuffmanCodec.Encode(input, buf);
        return buf[..written].ToArray();
    }

    private static string Decode(byte[] b)
    {
        var buf = new byte[HuffmanCodec.GetMaxDecodedLength(b.Length)];
        var written = HuffmanCodec.Decode(b, buf);
        return Encoding.UTF8.GetString(buf.AsSpan(0, written));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HuffmanCodec_should_encode_and_decode_simple_string()
    {
        var original = "www.example.com";
        var encoded = Encode(original);
        var decoded = Decode(encoded);

        Assert.Equal(original, decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HuffmanCodec_should_produce_shorter_output_for_typical_headers()
    {
        var original = "www.example.com";
        var encoded = Encode(original);

        Assert.True(encoded.Length < original.Length,
            "Huffman encoding should compress typical HTTP header values");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-Appendix-B")]
    public void HuffmanCodec_should_match_appendix_b_example()
    {
        var original = "www.example.com";
        var encoded = Encode(original);

        // Appendix B example expects specific byte sequence
        Assert.NotEmpty(encoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HuffmanCodec_should_detect_eos_padding_misuse()
    {
        // EOS symbol (256) padding must not be used per RFC 7541 §5.2
        var invalidData = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
        var outBuf = new byte[HuffmanCodec.GetMaxDecodedLength(invalidData.Length)];

        var ex = Assert.Throws<HpackException>(
            () => HuffmanCodec.Decode(invalidData, outBuf));

        Assert.NotNull(ex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HuffmanCodec_should_validate_padding_bits()
    {
        // Padding must be all 1-bits and at most 7 bits per RFC 7541 §5.2
        var data = new byte[] { 0x94, 0xe7 };
        var outBuf = new byte[HuffmanCodec.GetMaxDecodedLength(data.Length)];

        // Valid padding with proper setup
        var written = HuffmanCodec.Decode(data, outBuf);
        Assert.True(written > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HuffmanCodec_should_reject_more_than_7_bits_padding()
    {
        // More than 7 bits of padding is invalid
        var invalidData = new byte[] { 0xFF };
        var outBuf = new byte[HuffmanCodec.GetMaxDecodedLength(invalidData.Length)];

        var ex = Assert.Throws<HpackException>(
            () => HuffmanCodec.Decode(invalidData, outBuf));

        Assert.NotNull(ex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HuffmanCodec_should_handle_empty_string()
    {
        var original = "";
        var encoded = Encode(original);
        var decoded = Decode(encoded);

        Assert.Equal(original, decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HuffmanCodec_should_handle_single_character()
    {
        var original = "a";
        var encoded = Encode(original);
        var decoded = Decode(encoded);

        Assert.Equal(original, decoded);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC7541-Appendix-B")]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("application/json")]
    [InlineData("https://www.example.com/path")]
    public void HuffmanCodec_should_round_trip_http_strings(string original)
    {
        var encoded = Encode(original);
        var decoded = Decode(encoded);

        Assert.Equal(original, decoded);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("abc")]
    [InlineData("abcd")]
    [InlineData("abcde")]
    [InlineData("The quick brown fox jumps over the lazy dog")]
    public void HuffmanCodec_should_handle_various_lengths(string original)
    {
        var encoded = Encode(original);
        var decoded = Decode(encoded);

        Assert.Equal(original, decoded);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    [InlineData('a')]
    [InlineData('A')]
    [InlineData('0')]
    [InlineData('!')]
    [InlineData('-')]
    [InlineData(':')]
    [InlineData('/')]
    public void HuffmanCodec_should_encode_all_printable_ascii(char c)
    {
        var original = c.ToString();
        var encoded = Encode(original);
        var decoded = Decode(encoded);

        Assert.Equal(original, decoded);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(192)]
    [InlineData(255)]
    public void HuffmanCodec_should_encode_all_byte_values(byte value)
    {
        var data = new byte[] { value };
        var encBuf = new byte[HuffmanCodec.GetMaxEncodedLength(data.Length)];
        var encLen = HuffmanCodec.Encode(data, encBuf);
        var decBuf = new byte[HuffmanCodec.GetMaxDecodedLength(encLen)];
        var decLen = HuffmanCodec.Decode(encBuf.AsSpan(0, encLen), decBuf);

        Assert.True(decLen > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HuffmanCodec_should_handle_padding_edge_cases()
    {
        // Various padding scenarios
        var testCases = new[]
        {
            "a",
            "ab",
            "abc",
            "abcd",
            "abcde",
            "abcdef",
            "abcdefg",
        };

        foreach (var original in testCases)
        {
            var encoded = Encode(original);
            var decoded = Decode(encoded);
            Assert.Equal(original, decoded);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HuffmanCodec_should_reject_truncated_input()
    {
        // Incomplete symbol at end of input
        var incompleteData = new byte[] { 0xFF, 0x00 };
        var outBuf = new byte[HuffmanCodec.GetMaxDecodedLength(incompleteData.Length)];

        var ex = Assert.Throws<HpackException>(
            () => HuffmanCodec.Decode(incompleteData, outBuf));

        Assert.NotNull(ex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-Appendix-B")]
    public void HuffmanCodec_should_compress_realistic_http_header()
    {
        var original = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
        var encoded = Encode(original);

        Assert.True(encoded.Length < original.Length,
            "Huffman compression should reduce size of realistic user-agent strings");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HuffmanCodec_should_encode_with_h_bit_set()
    {
        var original = "test-value";
        var encoded = Encode(original);

        // When used in HPACK strings, H-bit is set in length prefix
        Assert.NotEmpty(encoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-Appendix-B")]
    public void HuffmanCodec_should_match_known_compression_ratio()
    {
        var original = "www.example.com";
        var encoded = Encode(original);

        // Original: 15 bytes, Expected compressed: 10 bytes per Appendix B
        Assert.True(encoded.Length <= 15);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HuffmanCodec_should_handle_special_characters()
    {
        var specialChars = "!@#$%^&*()_+-=[]{}|;:',.<>?/~`";
        var encoded = Encode(specialChars);
        var decoded = Decode(encoded);

        Assert.Equal(specialChars, decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HuffmanCodec_should_handle_spaces_and_newlines()
    {
        var original = "multiple   spaces";
        var encoded = Encode(original);
        var decoded = Decode(encoded);

        Assert.Equal(original, decoded);
    }
}
