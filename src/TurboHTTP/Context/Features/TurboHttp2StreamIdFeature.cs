using Microsoft.AspNetCore.Server.Kestrel.Core.Features;

namespace TurboHTTP.Context.Features;

internal sealed class TurboHttp2StreamIdFeature(int streamId) : IHttp2StreamIdFeature
{
    public int StreamId { get; } = streamId;
}
