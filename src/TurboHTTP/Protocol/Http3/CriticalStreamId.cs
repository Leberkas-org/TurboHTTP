using Servus.Akka.Transport;

namespace TurboHTTP.Protocol.Http3;

internal static class CriticalStreamId
{
    internal const long ControlId = -2;
    internal const long QpackEncoderId = -3;
    internal const long QpackDecoderId = -4;
    internal const long PushId = -5;

    public static readonly StreamTarget Control = new(ControlId);
    public static readonly StreamTarget QpackEncoder = new(QpackEncoderId);
    public static readonly StreamTarget QpackDecoder = new(QpackDecoderId);
    public static readonly StreamTarget Push = new(PushId);

    public static bool IsCritical(long streamId) => streamId is ControlId or QpackEncoderId or QpackDecoderId;
}
