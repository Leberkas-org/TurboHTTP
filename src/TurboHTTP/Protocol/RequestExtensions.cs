using TurboHTTP.Internal;

namespace TurboHTTP.Protocol;

internal static class RequestFault
{
    public static void Fail(this HttpRequestMessage request, Exception exception)
    {
        if (request.Options.TryGetValue(TurboClientCorrelation.Key, out var pending))
        {
            pending.TrySetException(exception);
        }
    }

    public static void FailAll(IEnumerable<HttpRequestMessage> requests, Exception exception)
    {
        foreach (var request in requests)
        {
            request.Fail(exception);
        }
    }

    public static void FailAll(Queue<HttpRequestMessage> queue, Exception exception)
    {
        while (queue.Count > 0)
        {
            queue.Dequeue().Fail(exception);
        }
    }
}