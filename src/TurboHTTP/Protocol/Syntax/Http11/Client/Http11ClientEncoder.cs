using Akka.Actor;
using TurboHTTP.Protocol.LineBased;
using TurboHTTP.Protocol.LineBased.Body;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http11.Options;

namespace TurboHTTP.Protocol.Syntax.Http11.Client;

internal sealed class Http11ClientEncoder
{
    private readonly Http11ClientEncoderOptions _options;
    private readonly HeaderCollection _reusableHeaders = new();

    public Http11ClientEncoder(Http11ClientEncoderOptions options)
    {
        options.Validate();
        _options = options;
    }

    public int Encode(Span<byte> destination, HttpRequestMessage request, IActorRef stageActor)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RequestUri);

        RequestValidator.Validate(request);

        var contentLength = request.Content?.Headers.ContentLength;
        var bodyStream = request.Content?.ReadAsStream();
        var bodyEncoder = BodyEncoderFactory.Create(bodyStream, contentLength, request.Version);

        var writer = SpanWriter.Create(destination);
        var targetStr = request.ResolveTarget();
        RequestLineWriter.Write(ref writer, request.Method.Method, targetStr, request.Version);
        HeaderBuilder.Build(request, _options, _reusableHeaders);
        HeaderBlockWriter.Write(ref writer, _reusableHeaders);

        bodyEncoder?.Start(bodyStream!, stageActor);

        return writer.BytesWritten;
    }
}