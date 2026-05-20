using TurboHTTP.Diagnostics;

namespace TurboHTTP.Tests.Diagnostics;

public sealed class HexDumpFormatterSpec
{
    [Fact(Timeout = 5000)]
    public void Format_should_return_empty_string_for_empty_input()
    {
        var result = HexDumpFormatter.Format(ReadOnlySpan<byte>.Empty);
        Assert.Equal(string.Empty, result);
    }

    [Fact(Timeout = 5000)]
    public void Format_should_format_partial_line()
    {
        var data = "Hello"u8;
        var result = HexDumpFormatter.Format(data);

        Assert.Contains("48 65 6C 6C 6F", result);
        Assert.Contains("Hello", result);
        Assert.Contains("00000000", result);
    }

    [Fact(Timeout = 5000)]
    public void Format_should_format_exact_16_byte_line()
    {
        var data = "0123456789ABCDEF"u8;
        var result = HexDumpFormatter.Format(data);

        var lines = result.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.Contains("00000000", lines[0]);
        Assert.Contains("0123456789ABCDEF", lines[0]);
    }

    [Fact(Timeout = 5000)]
    public void Format_should_produce_multiple_lines_for_large_input()
    {
        var data = new byte[32];
        for (var i = 0; i < 32; i++)
        {
            data[i] = (byte)(0x41 + (i % 26));
        }

        var result = HexDumpFormatter.Format(data);
        var lines = result.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Contains("00000000", lines[0]);
        Assert.Contains("00000010", lines[1]);
    }

    [Fact(Timeout = 5000)]
    public void Format_should_show_non_printable_chars_as_dot()
    {
        var data = new byte[] { 0x00, 0x01, 0x1F, 0x7F, 0x80, 0xFF, 0x41, 0x42 };
        var result = HexDumpFormatter.Format(data);

        Assert.Contains("......AB", result);
    }

    [Fact(Timeout = 5000)]
    public void Format_should_separate_two_8_byte_groups_with_extra_space()
    {
        var data = new byte[16];
        var result = HexDumpFormatter.Format(data);

        Assert.Contains("00 00 00 00 00 00 00 00  00 00 00 00 00 00 00 00", result);
    }
}
