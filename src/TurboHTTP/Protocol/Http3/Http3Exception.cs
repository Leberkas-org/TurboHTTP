namespace TurboHTTP.Protocol.Http3;

/// <summary>
/// Thrown when an HTTP/3 protocol error is detected.
/// Carries the appropriate <see cref="Http3ErrorCode"/> so the transport
/// layer can close the connection with the correct error code.
/// </summary>
internal sealed class Http3Exception(Http3ErrorCode errorCode, string message) : TurboProtocolException(message)
{
    public Http3ErrorCode ErrorCode { get; } = errorCode;
}