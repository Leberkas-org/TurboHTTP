using System;

namespace TurboHttp;

/// <summary>
/// Base class for all TurboHttp exceptions.
/// Catch this type to handle any error originating from the TurboHttp library.
/// </summary>
public abstract class TurboHttpException : Exception
{
    protected TurboHttpException() { }

    protected TurboHttpException(string message) : base(message) { }

    protected TurboHttpException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Base class for protocol-level exceptions (RFC violations, malformed frames, compression errors).
/// Catch this type to handle any protocol error across HTTP/1.x, HTTP/2, HTTP/3, HPACK, and QPACK.
/// </summary>
public abstract class TurboProtocolException : TurboHttpException
{
    protected TurboProtocolException() { }

    protected TurboProtocolException(string message) : base(message) { }

    protected TurboProtocolException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Base class for transport-level exceptions (connection failures, abrupt disconnects).
/// Catch this type to handle any connection or transport error.
/// </summary>
public abstract class TurboTransportException : TurboHttpException
{
    protected TurboTransportException() { }

    protected TurboTransportException(string message) : base(message) { }

    protected TurboTransportException(string message, Exception innerException) : base(message, innerException) { }
}
