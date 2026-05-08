using TurboHTTP.Internal;

namespace TurboHTTP;

public static class Extensions
{
    public static ValueTask<HttpResponseMessage> GetResponseAsync(this HttpRequestMessage request,
        CancellationToken ct = default)
    {
        var pending = PendingRequest.Rent();
        request.Options.Set(TurboClientCorrelation.Key, pending);
        request.Options.Set(TurboClientCorrelation.VersionKey, pending.Version);

        if (ct.CanBeCanceled)
        {
            ct.UnsafeRegister(
                static (state, ct) => ((PendingRequest)state!).TrySetCanceled(ct),
                pending);
        }

        return pending.GetValueTask();
    }
}