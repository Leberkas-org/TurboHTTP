using System.Net.Http.Headers;
using System.Text;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http11.Options;

namespace TurboHTTP.Protocol.Syntax.Http11.Client;

internal static class HeaderBuilder
{
    public static HeaderCollection Build(HttpRequestMessage request, Http11ClientEncoderOptions options)
    {
        var collection = new HeaderCollection();
        Build(request, options, collection);
        return collection;
    }

    public static void Build(HttpRequestMessage request, Http11ClientEncoderOptions options, HeaderCollection target)
    {
        target.Clear();

        if (options.AutoHost)
        {
            AddHostHeader(target, request.RequestUri!);
        }

        var isChunked = request.Headers.TransferEncodingChunked == true;
        if (!isChunked && request.Content is not null && request.Content.Headers.ContentLength is null)
        {
            isChunked = true;
            request.Headers.TransferEncodingChunked = true;
        }

        if (options.AutoAcceptEncoding)
        {
            AddAcceptEncodingIfNeeded(target, request.Headers);
        }

        AddHeaders(target, request.Headers, skipHost: true);

        if (request.Content != null)
        {
            AddContentHeaders(target, request.Content.Headers, isChunked);
        }

        AddConnectionHeader(target, request.Headers);
    }

    private static void AddHostHeader(HeaderCollection collection, Uri uri)
    {
        var host = uri.IsDefaultPort
            ? uri.Host
            : string.Concat(uri.Host, WellKnownHeaders.Colon, uri.Port.ToString());

        collection.Add(WellKnownHeaders.Host, host);
    }

    private static void AddAcceptEncodingIfNeeded(HeaderCollection collection, HttpRequestHeaders headers)
    {
        if (headers.AcceptEncoding.Count > 0)
        {
            return;
        }

        collection.Add(WellKnownHeaders.AcceptEncoding,
            WellKnownHeaders.GzipValue + WellKnownHeaders.CommaSpace + WellKnownHeaders.DeflateValue +
            WellKnownHeaders.CommaSpace + WellKnownHeaders.BrValue);
    }

    private static void AddHeaders(HeaderCollection collection,
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers, bool skipHost)
    {
        foreach (var header in headers)
        {
            if (skipHost && header.Key.Equals(WellKnownHeaders.Host.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (header.Key.Equals(WellKnownHeaders.Connection.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsConnectionSpecificHeader(header.Key))
            {
                continue;
            }

            if (header.Key.Equals(WellKnownHeaders.Te.Name, StringComparison.OrdinalIgnoreCase))
            {
                AddTeHeader(collection, header.Value);
                continue;
            }

            AddHeader(collection, header.Key, header.Value);
        }
    }

    private static void AddHeader(HeaderCollection collection, string name, IEnumerable<string> values)
    {
        string? combined = null;
        StringBuilder? sb = null;

        foreach (var value in values)
        {
            if (combined is null)
            {
                combined = value;
            }
            else
            {
                sb ??= new StringBuilder(combined);
                sb.Append(WellKnownHeaders.CommaSpace).Append(value);
            }
        }

        if (combined is null)
        {
            return;
        }

        collection.Add(name, sb?.ToString() ?? combined);
    }

    private static void AddTeHeader(HeaderCollection collection, IEnumerable<string> values)
    {
        var validTokens = new List<string>();

        foreach (var value in values)
        {
            var span = value.AsSpan();
            var start = 0;
            while (true)
            {
                var comma = span[start..].IndexOf(WellKnownHeaders.Comma);
                var end = comma >= 0 ? start + comma : span.Length;
                var token = span[start..end].Trim();

                if (token.Length > 0 &&
                    !token.Equals(WellKnownHeaders.ChunkedValue, StringComparison.OrdinalIgnoreCase))
                {
                    validTokens.Add(token.ToString());
                }

                if (comma < 0)
                {
                    break;
                }

                start = end + 1;
            }
        }

        if (validTokens.Count == 0)
        {
            return;
        }

        collection.Add(WellKnownHeaders.Te, string.Join(WellKnownHeaders.CommaSpace, validTokens));
    }

    private static void AddContentHeaders(HeaderCollection collection, HttpContentHeaders headers, bool isChunked)
    {
        foreach (var header in headers)
        {
            if (isChunked && header.Key.Equals(WellKnownHeaders.ContentLength, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddHeader(collection, header.Key, header.Value);
        }
    }

    private static void AddConnectionHeader(HeaderCollection collection, HttpRequestHeaders headers)
    {
        var hasTeValues = HasNonChunkedTeValues(headers);

        if (ContainsToken(headers.Connection, WellKnownHeaders.CloseValue))
        {
            if (hasTeValues && !ContainsToken(headers.Connection, WellKnownHeaders.Te))
            {
                collection.Add(WellKnownHeaders.Connection,
                    WellKnownHeaders.CloseValue + WellKnownHeaders.CommaSpace + WellKnownHeaders.Te);
            }
            else
            {
                collection.Add(WellKnownHeaders.Connection, WellKnownHeaders.CloseValue);
            }

            return;
        }

        string? combined = null;
        StringBuilder? sb = null;
        var alreadyHasTe = false;

        foreach (var value in headers.Connection)
        {
            if (combined is null)
            {
                combined = value;
            }
            else
            {
                sb ??= new StringBuilder(combined);
                sb.Append(WellKnownHeaders.CommaSpace).Append(value);
            }

            if (value.Equals(WellKnownHeaders.Te, StringComparison.OrdinalIgnoreCase))
            {
                alreadyHasTe = true;
            }
        }

        if (hasTeValues && !alreadyHasTe)
        {
            if (combined is null)
            {
                combined = WellKnownHeaders.Te;
            }
            else
            {
                sb ??= new StringBuilder(combined);
                sb.Append(WellKnownHeaders.CommaSpace).Append(WellKnownHeaders.Te);
            }
        }

        if (combined is null)
        {
            combined = WellKnownHeaders.KeepAliveValue;
        }
        else
        {
            sb ??= new StringBuilder(combined);
            sb.Append(WellKnownHeaders.CommaSpace).Append(WellKnownHeaders.KeepAliveValue);
        }

        collection.Add(WellKnownHeaders.Connection, sb?.ToString() ?? combined);
    }

    private static bool HasNonChunkedTeValues(HttpRequestHeaders headers)
    {
        if (!headers.TryGetValues(WellKnownHeaders.Te, out var teValues))
        {
            return false;
        }

        foreach (var value in teValues)
        {
            var span = value.AsSpan();
            var start = 0;
            while (true)
            {
                var comma = span[start..].IndexOf(WellKnownHeaders.Comma);
                var end = comma >= 0 ? start + comma : span.Length;
                var token = span[start..end].Trim();

                if (token.Length > 0 &&
                    !token.Equals(WellKnownHeaders.ChunkedValue, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (comma < 0)
                {
                    break;
                }

                start = end + 1;
            }
        }

        return false;
    }

    private static bool IsConnectionSpecificHeader(string headerName)
    {
        return headerName.Equals(WellKnownHeaders.Trailer, StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals(WellKnownHeaders.KeepAliveHeader, StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals(WellKnownHeaders.Upgrade, StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals(WellKnownHeaders.ProxyConnection, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsToken(HttpHeaderValueCollection<string> values, string token)
    {
        foreach (var value in values)
        {
            if (value.Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}