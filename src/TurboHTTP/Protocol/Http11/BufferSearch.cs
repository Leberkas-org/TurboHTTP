namespace TurboHTTP.Protocol.Http11;

internal static class BufferSearch
{
    internal static int FindCrlfCrlf(ReadOnlySpan<byte> span)
    {
        var search = span;
        var offset = 0;

        while (search.Length >= 4)
        {
            var idx = search.IndexOf((byte)'\r');
            if (idx < 0 || idx + 3 >= search.Length)
            {
                return -1;
            }

            if (search[idx + 1] == '\n' && search[idx + 2] == '\r' && search[idx + 3] == '\n')
            {
                return offset + idx;
            }

            search = search[(idx + 1)..];
            offset += idx + 1;
        }

        return -1;
    }

    internal static int FindCrlf(ReadOnlySpan<byte> span, int start)
    {
        var search = span[start..];
        var offset = start;

        while (search.Length >= 2)
        {
            var idx = search.IndexOf((byte)'\r');
            if (idx < 0 || idx + 1 >= search.Length)
            {
                return -1;
            }

            if (search[idx + 1] == '\n')
            {
                return offset + idx;
            }

            search = search[(idx + 1)..];
            offset += idx + 1;
        }

        return -1;
    }

    internal static bool TryParseInt(ReadOnlySpan<byte> span, out int value)
    {
        value = 0;
        foreach (var b in span)
        {
            if (b < '0' || b > '9')
            {
                return false;
            }

            value = value * 10 + (b - '0');
        }

        return span.Length > 0;
    }

    internal static bool TryParseHex(ReadOnlySpan<byte> span, out int value)
    {
        value = 0;
        foreach (var b in span)
        {
            int digit;
            if (b >= '0' && b <= '9')
            {
                digit = b - '0';
            }
            else if (b >= 'a' && b <= 'f')
            {
                digit = b - 'a' + 10;
            }
            else if (b >= 'A' && b <= 'F')
            {
                digit = b - 'A' + 10;
            }
            else
            {
                return false;
            }

            // Detect overflow: if top 4 bits are non-zero, shifting left 4 would overflow int
            if (value >> 28 != 0)
            {
                return false;
            }

            value = (value << 4) | digit;
        }

        return span.Length > 0;
    }
}
