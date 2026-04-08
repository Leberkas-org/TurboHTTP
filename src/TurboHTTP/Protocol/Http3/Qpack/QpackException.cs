namespace TurboHTTP.Protocol.Http3.Qpack;

/// <summary>
/// Exception thrown for QPACK protocol violations (RFC 9204).
/// </summary>
public sealed class QpackException : TurboProtocolException
{
    public QpackException(string message) : base(message)
    {
    }

    public QpackException(string message, Exception inner) : base(message, inner)
    {
    }
}
