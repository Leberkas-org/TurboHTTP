using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Protocol.Syntax.Http3.Server;

internal sealed class Http3ServerDecoder
{
    private const string PseudoHeaderSection = "RFC 9114 §4.3.1";
    private const string UppercaseSection = "RFC 9114 §4.2";
    private const string TokenSection = "RFC 9114 §10.3";
    private const string FieldValueSection = "RFC 9114 §10.3";
    private const string ConnectionSection = "RFC 9114 §4.2";

    private readonly QpackTableSync _tableSync;
    private readonly int _maxFieldSectionSize;

    public Http3ServerDecoder(QpackTableSync tableSync, int maxFieldSectionSize = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(tableSync);
        _tableSync = tableSync;
        _maxFieldSectionSize = maxFieldSectionSize;
    }

    public ReadOnlyMemory<byte> DecoderInstructions => _tableSync.Decoder.DecoderInstructions;

    public TurboHttpRequestFeature? DecodeHeadersToFeature(HeadersFrame frame, StreamState state, bool endStream)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(state);

        var result = _tableSync.TryDecodeOrBlock(frame.HeaderBlock, (int)state.StreamId);

        if (result.IsBlocked)
        {
            return null;
        }

        var headers = result.Headers!;
        ValidateRequestHeaders(headers);
        ValidateFieldSectionSize(headers, state.StreamId);

        var feature = new TurboHttpRequestFeature
        {
            Protocol = "HTTP/3"
        };

        var isConnect = false;

        foreach (var (name, value) in headers)
        {
            if (name == WellKnownHeaders.Method)
            {
                feature.Method = value;
                if (value == WellKnownHeaders.Connect)
                {
                    isConnect = true;
                }
            }
            else if (name == WellKnownHeaders.Path)
            {
                state.AddPseudoHeader(WellKnownHeaders.Path, value);
                feature.Path = value;
            }
            else if (name == WellKnownHeaders.Scheme)
            {
                state.AddPseudoHeader(WellKnownHeaders.Scheme, value);
                feature.Scheme = value;
            }
            else if (name == WellKnownHeaders.Authority)
            {
                state.AddPseudoHeader(WellKnownHeaders.Authority, value);
                feature.ExtractedHost = value;
            }
            else if (!name.StartsWith(':'))
            {
                feature.Headers[name] = value;

                if (ContentHeaderClassifier.IsContentHeader(name))
                {
                    state.AddContentHeader(name, value);
                }
            }
        }

        if (!isConnect)
        {
            var path = state.GetPseudoHeader(WellKnownHeaders.Path);
            var scheme = state.GetPseudoHeader(WellKnownHeaders.Scheme);
            var authority = state.GetPseudoHeader(WellKnownHeaders.Authority);

            feature.RawTarget = path;
            feature.QueryString = ParseQueryString(path);
            feature.Path = ParsePath(path);
        }

        state.InitRequestFeature(feature);

        if (!endStream)
        {
            return null;
        }

        return feature;
    }

    internal static void ValidateRequestHeaders(IReadOnlyList<(string Name, string Value)> headers)
    {
        PseudoHeaderValidator.ValidateRequestPseudoHeaders(
            headers,
            static h => h.Name,
            static h => h.Value,
            PseudoHeaderSection);

        Semantics.FieldValidator.Validate(
            headers,
            static h => h.Name,
            static h => h.Value,
            UppercaseSection,
            TokenSection,
            FieldValueSection,
            ConnectionSection);
    }

    private void ValidateFieldSectionSize(IReadOnlyList<(string Name, string Value)> headers, long streamId)
    {
        if (_maxFieldSectionSize == int.MaxValue)
        {
            return;
        }

        var totalSize = 0L;
        foreach (var (name, value) in headers)
        {
            totalSize += name.Length + value.Length + 32;
        }

        if (totalSize > _maxFieldSectionSize)
        {
            throw new HttpProtocolException(
                "RFC 9114 §4.2.2: Received field section exceeds SETTINGS_MAX_FIELD_SECTION_SIZE");
        }
    }

    private static string ParsePath(string target)
    {
        var queryIdx = target.IndexOf('?');
        var pathPart = queryIdx >= 0 ? target[..queryIdx] : target;
        return string.IsNullOrEmpty(pathPart) ? "/" : pathPart;
    }

    private static string ParseQueryString(string target)
    {
        var queryIdx = target.IndexOf('?');
        return queryIdx >= 0 ? target[queryIdx..] : string.Empty;
    }
}
