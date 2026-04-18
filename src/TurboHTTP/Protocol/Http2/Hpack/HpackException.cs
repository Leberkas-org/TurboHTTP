namespace TurboHTTP.Protocol.Http2.Hpack;

/// <summary>
/// HPACK-specific exception for RFC 7541 protocol violations.
/// </summary>
internal sealed class HpackException(string message) : TurboProtocolException(message);
