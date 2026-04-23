using System.Text;

namespace TurboHTTP.Protocol.Http11;

internal static class StatusLineDecoder
{
    internal static bool TryParse(ReadOnlySpan<byte> line, out int statusCode, out string reasonPhrase)
    {
        statusCode = 0;
        reasonPhrase = string.Empty;

        // Format: HTTP/1.1 200 OK
        // Minimum: "HTTP/1.1 200" = 12 chars
        if (line.Length < 12)
        {
            return false;
        }

        // Check HTTP version prefix
        if (!line.StartsWith("HTTP/1."u8))
        {
            return false;
        }

        // Find first space after version
        var firstSpace = line.IndexOf((byte)' ');
        if (firstSpace < 8)
        {
            return false;
        }

        // Parse status code (3 digits)
        var codeStart = firstSpace + 1;
        if (codeStart + 3 > line.Length)
        {
            return false;
        }

        var codeSpan = line.Slice(codeStart, 3);
        if (!BufferSearch.TryParseInt(codeSpan, out statusCode))
        {
            return false;
        }

        // Parse reason phrase (optional)
        var reasonStart = codeStart + 4; // "200 "
        if (reasonStart < line.Length)
        {
            reasonPhrase = GetOrCreateReasonPhrase(line[reasonStart..]);
        }

        return statusCode is >= 100 and < 600;
    }

    internal static int? PeekCode(ReadOnlySpan<byte> buffer)
    {
        // Status line format: HTTP/1.1 200 OK\r\n
        // Minimum: "HTTP/1.1 NNN" = 12 bytes
        if (buffer.Length < 12)
        {
            return null;
        }

        // Find the space after version (position 8 for "HTTP/1.1 ")
        var spaceIdx = buffer.IndexOf((byte)' ');
        if (spaceIdx < 0 || spaceIdx + 4 > buffer.Length)
        {
            return null;
        }

        var codeSlice = buffer.Slice(spaceIdx + 1, 3);
        if (codeSlice[0] < (byte)'1' || codeSlice[0] > (byte)'5')
        {
            return null;
        }

        var code = (codeSlice[0] - '0') * 100 + (codeSlice[1] - '0') * 10 + (codeSlice[2] - '0');
        return code;
    }

    private static string GetOrCreateReasonPhrase(ReadOnlySpan<byte> span)
        => span.Length switch
        {
            2 => span.SequenceEqual("OK"u8) ? "OK" : Encoding.ASCII.GetString(span),
            5 => span.SequenceEqual("Found"u8) ? "Found" : Encoding.ASCII.GetString(span),
            7 => span.SequenceEqual("Created"u8) ? "Created" : Encoding.ASCII.GetString(span),
            8 => span.SequenceEqual("Accepted"u8) ? "Accepted" : Encoding.ASCII.GetString(span),
            9 => span.SequenceEqual("Not Found"u8) ? "Not Found" :
                  span.SequenceEqual("Forbidden"u8) ? "Forbidden" : Encoding.ASCII.GetString(span),
            10 => span.SequenceEqual("No Content"u8) ? "No Content" : Encoding.ASCII.GetString(span),
            11 => span.SequenceEqual("Bad Request"u8) ? "Bad Request" : Encoding.ASCII.GetString(span),
            12 => span.SequenceEqual("Unauthorized"u8) ? "Unauthorized" :
                  span.SequenceEqual("Not Modified"u8) ? "Not Modified" : Encoding.ASCII.GetString(span),
            15 => span.SequenceEqual("Partial Content"u8) ? "Partial Content" : Encoding.ASCII.GetString(span),
            17 => span.SequenceEqual("Moved Permanently"u8) ? "Moved Permanently" : Encoding.ASCII.GetString(span),
            21 => span.SequenceEqual("Internal Server Error"u8) ? "Internal Server Error" : Encoding.ASCII.GetString(span),
            _ => Encoding.ASCII.GetString(span),
        };
}
