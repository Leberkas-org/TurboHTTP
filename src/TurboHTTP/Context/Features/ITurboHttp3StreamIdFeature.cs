namespace TurboHTTP.Context.Features;

internal interface ITurboHttp3StreamIdFeature
{
    long StreamId { get; }
}

internal sealed class TurboHttp3StreamIdFeature(long streamId) : ITurboHttp3StreamIdFeature
{
    public long StreamId { get; } = streamId;
}
