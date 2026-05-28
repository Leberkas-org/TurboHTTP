namespace TurboHTTP.Internal;

internal static class OptionsKey
{
    internal static readonly HttpRequestOptionsKey<Guid> ConsumerIdKey = new("TurboHTTP.ConsumerId");
    internal static readonly HttpRequestOptionsKey<PendingRequest> Key = new("TurboHTTP.PendingRequest");
    internal static readonly HttpRequestOptionsKey<short> VersionKey = new("TurboHTTP.Version");
}