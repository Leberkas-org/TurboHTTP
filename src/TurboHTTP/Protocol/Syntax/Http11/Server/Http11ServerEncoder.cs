using System.Globalization;
using System.Net;
using Akka.Actor;
using TurboHTTP.Protocol.LineBased;
using TurboHTTP.Protocol.LineBased.Body;
using TurboHTTP.Protocol.Syntax.Http11.Options;

namespace TurboHTTP.Protocol.Syntax.Http11.Server;

internal sealed class Http11ServerEncoder
{
    private readonly Http11ServerEncoderOptions _options;
    private IBodyEncoder? _activeBodyEncoder;

    public Http11ServerEncoder(Http11ServerEncoderOptions options)
    {
        options.Validate();
        _options = options;
    }

    public int Encode(Span<byte> destination, HttpResponseMessage response, IActorRef stageActor,
        bool isChunked = false, bool connectionClose = false)
    {
        var writer = SpanWriter.Create(destination);

        StatusLineWriter.Write(ref writer, HttpVersion.Version11, (int)response.StatusCode);

        var headers = response.GetHeaderCollection();

        if (isChunked)
        {
            headers.Add(WellKnownHeaders.TransferEncoding, WellKnownHeaders.ChunkedValue);
        }
        else
        {
            var contentLength = response.Content.Headers.ContentLength ?? 0L;
            headers.Add(WellKnownHeaders.ContentLength, contentLength.ToString(CultureInfo.InvariantCulture));
        }

        if (_options.WriteDateHeader && !headers.Contains(WellKnownHeaders.Date))
        {
            headers.Add(WellKnownHeaders.Date, DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture));
        }

        if (connectionClose)
        {
            headers.Add(WellKnownHeaders.Connection, WellKnownHeaders.CloseValue);
        }

        HeaderBlockWriter.Write(ref writer, headers);

        _activeBodyEncoder = BodyEncoderFactory.Create(response.Content, HttpVersion.Version11);
        _activeBodyEncoder?.Start(response.Content, stageActor);

        return writer.BytesWritten;
    }

    public void CancelActiveBody()
    {
        _activeBodyEncoder?.Dispose();
        _activeBodyEncoder = null;
    }
}