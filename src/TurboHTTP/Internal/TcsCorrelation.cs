namespace TurboHTTP.Internal;

/// <summary>
/// Shared keys used to inject a <see cref="PendingRequest"/> and its MRVTSC version token
/// directly into <see cref="HttpRequestMessage.Options"/> so the pipeline Sink can complete
/// it without a dictionary lookup (G2). The version token prevents stale completions when
/// a pooled <see cref="PendingRequest"/> is reused for a new request (E4).
/// </summary>
internal static class TcsCorrelation
{
    internal static readonly HttpRequestOptionsKey<PendingRequest> Key = new("_tcs");
    internal static readonly HttpRequestOptionsKey<short> VersionKey = new("_tcs_ver");
}
