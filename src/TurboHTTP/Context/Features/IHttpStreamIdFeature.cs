namespace TurboHTTP.Context.Features;

internal interface IHttpStreamIdFeature
{
    long StreamId { get; }
}

internal sealed class TurboStreamIdFeature(long streamId) : IHttpStreamIdFeature
{
    public long StreamId { get; } = streamId;
}
