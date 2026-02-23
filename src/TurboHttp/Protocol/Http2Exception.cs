using System;

namespace TurboHttp.Protocol;

public sealed class Http2Exception(string message, Http2ErrorCode errorCode = Http2ErrorCode.ProtocolError)
    : Exception(message)
{
    public Http2ErrorCode ErrorCode { get; } = errorCode;
}