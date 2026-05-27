using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Streams.IO;
using Servus.Akka.Sse;
using TurboHTTP.Internal;

namespace TurboHTTP.Client;

public static class Extensions
{
    public static ValueTask<HttpResponseMessage> GetResponseAsync(this HttpRequestMessage request,
        CancellationToken ct = default)
    {
        var pending = PendingRequest.Rent();
        request.Options.Set(OptionsKey.Key, pending);
        request.Options.Set(OptionsKey.VersionKey, pending.Version);

        if (ct.CanBeCanceled)
        {
            ct.UnsafeRegister(
                static (state, ct) => ((PendingRequest)state!).TrySetCanceled(ct),
                pending);
        }

        return pending.GetValueTask();
    }

    /// <summary>
    /// Converts an HttpResponseMessage content stream into a reactive Source of ServerSentEvent.
    /// Uses the SSE parser GraphStage to parse binary content into structured events.
    /// </summary>
    /// <param name="response">The HTTP response message containing SSE data</param>
    /// <returns>Source that emits ServerSentEvent records from the response body</returns>
    /// <remarks>
    /// The returned Source reads from the response content stream and parses SSE
    /// according to RFC 9110. The response must have a stream-compatible content.
    /// </remarks>
    public static Source<ServerSentEvent, NotUsed> AsEventStream(this HttpResponseMessage response)
    {
        return StreamSource.From(response.Content.ReadAsStream())
            .Via(SseParserFlow.Instance);
    }
}