using System.Text;

namespace TurboHTTP.Diagnostics;

internal static class HexDumpFormatter
{
    private const int BytesPerLine = 16;
    private const int FirstGroupSize = 8;

    public static string Format(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return string.Empty;
        }

        var lineCount = (data.Length + BytesPerLine - 1) / BytesPerLine;
        var sb = new StringBuilder(lineCount * 78);

        for (var lineOffset = 0; lineOffset < data.Length; lineOffset += BytesPerLine)
        {
            if (lineOffset > 0)
            {
                sb.AppendLine();
            }

            var lineLength = Math.Min(BytesPerLine, data.Length - lineOffset);
            var line = data.Slice(lineOffset, lineLength);

            sb.Append(lineOffset.ToString("X8"));
            sb.Append("  ");

            for (var i = 0; i < BytesPerLine; i++)
            {
                if (i == FirstGroupSize)
                {
                    sb.Append(' ');
                }

                if (i < lineLength)
                {
                    sb.Append(line[i].ToString("X2"));
                    sb.Append(' ');
                }
                else
                {
                    sb.Append("   ");
                }
            }

            sb.Append(' ');

            for (var i = 0; i < lineLength; i++)
            {
                var b = line[i];
                sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }
        }

        return sb.ToString();
    }
}
