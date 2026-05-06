namespace TurboHTTP.Protocol.Http3;

/// <summary>
/// Thrown when an HTTP/3 protocol error is detected.
/// Carries the appropriate <see cref="Http3.ErrorCode"/> so the transport
/// layer can close the connection with the correct error code.
/// </summary>
internal sealed class Http3Exception(ErrorCode errorCode, string message) : TurboProtocolException(message)
{
    public ErrorCode ErrorCode { get; } = errorCode;
}