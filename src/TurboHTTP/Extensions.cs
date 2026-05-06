namespace TurboHTTP;

public static class Extensions
{
    public static void AddResponseTask(this HttpRequestMessage request, out ValueTask<HttpResponseMessage> responseTask,
        CancellationToken ct = default)
    {
        var pending = PendingRequest.Rent();
        request.Options.Set(TcsCorrelation.Key, pending);
        request.Options.Set(TcsCorrelation.VersionKey, pending.Version);

        try
        {
            if (!ct.CanBeCanceled)
            {
                responseTask = pending.GetValueTask();
            }

            using (ct.UnsafeRegister(
                       static (state, ct) => ((PendingRequest)state!).TrySetCanceled(ct),
                       pending))
            {
                responseTask = pending.GetValueTask();
            }
        }
        finally
        {
            PendingRequest.Return(pending);
        }
    }
}