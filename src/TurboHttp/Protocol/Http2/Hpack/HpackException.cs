namespace TurboHttp.Protocol.Http2.Hpack;

/// <summary>
/// HPACK-specific exception for RFC 7541 protocol violations.
/// </summary>
public sealed class HpackException : TurboProtocolException
{
    public HpackException(string message) : base(message)
    {
    }

    public HpackException(string message, Exception inner) : base(message, inner)
    {
    }
}
