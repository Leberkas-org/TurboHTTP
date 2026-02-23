using System;

namespace TurboHttp.Protocol;

/// <summary>
/// Thrown when an HTTP decoder encounters a protocol violation or malformed message.
/// The <see cref="DecodeError"/> property identifies the specific violation;
/// <see cref="Exception.Message"/> contains a human-readable description with an RFC reference.
/// </summary>
public sealed class HttpDecoderException : Exception
{
    /// <summary>The specific decode error that caused this exception.</summary>
    public HttpDecodeError DecodeError { get; }

    /// <summary>Creates an exception for the given error code with a default RFC-referenced message.</summary>
    public HttpDecoderException(HttpDecodeError error)
        : base(GetDefaultMessage(error))
    {
        DecodeError = error;
    }

    /// <summary>
    /// Creates an exception for the given error code, appending caller-supplied context
    /// (e.g. "Received 150 fields; limit is 100.") to the default RFC-referenced message.
    /// </summary>
    public HttpDecoderException(HttpDecodeError error, string context)
        : base($"{GetDefaultMessage(error)} {context}")
    {
        DecodeError = error;
    }

    /// <summary>
    /// Returns the default human-readable message for <paramref name="error"/>,
    /// including the relevant RFC section reference.
    /// </summary>
    internal static string GetDefaultMessage(HttpDecodeError error) => error switch
    {
        HttpDecodeError.NeedMoreData =>
            "More data required to complete parsing.",

        HttpDecodeError.InvalidStatusLine =>
            @"RFC 9112 §4: Invalid status-line. Expected 'HTTP/1.x NNN reason-phrase\r\n'.",

        HttpDecodeError.InvalidHeader =>
            @"RFC 9112 §5.1: Invalid header field. Expected 'name: value\r\n'; missing or misplaced colon separator.",

        HttpDecodeError.InvalidContentLength =>
            "RFC 9112 §6.3: Invalid Content-Length value. Must be a non-negative integer.",

        HttpDecodeError.InvalidChunkedEncoding =>
            "RFC 9112 §7.1: Invalid chunked transfer-encoding format.",

        HttpDecodeError.DecompressionFailed =>
            "Content decompression failed.",

        HttpDecodeError.LineTooLong =>
            "RFC 9112 §2.3: Line length exceeds the configured maximum.",

        HttpDecodeError.InvalidRequestLine =>
            @"RFC 9112 §3: Invalid request-line. Expected 'METHOD SP request-target SP HTTP/1.x\r\n'.",

        HttpDecodeError.InvalidMethodToken =>
            "RFC 9112 §3.1: Invalid HTTP method token. Methods must consist of token characters only.",

        HttpDecodeError.InvalidRequestTarget =>
            "RFC 9112 §3.2: Invalid request-target.",

        HttpDecodeError.InvalidHttpVersion =>
            "RFC 9112 §2.3: Invalid HTTP version. Expected 'HTTP/1.0' or 'HTTP/1.1'.",

        HttpDecodeError.MissingHostHeader =>
            "RFC 9112 §5.4: Missing required Host header in HTTP/1.1 request.",

        HttpDecodeError.MultipleHostHeaders =>
            "RFC 9112 §5.4: Multiple Host headers present; exactly one is required.",

        HttpDecodeError.MultipleContentLengthValues =>
            "RFC 9112 §6.3: Multiple Content-Length headers with conflicting values; request-smuggling risk.",

        HttpDecodeError.InvalidFieldName =>
            "RFC 9112 §5.1: Invalid header field name. Names must be token characters with no surrounding whitespace.",

        HttpDecodeError.InvalidFieldValue =>
            @"RFC 9112 §5.5: Invalid header field value. Values must not contain CR (\r), LF (\n), or NUL (\0) bytes.",

        HttpDecodeError.ObsoleteFoldingDetected =>
            "RFC 9112 §5.2: Obsolete line folding detected. Folded header values are not permitted.",

        HttpDecodeError.ChunkedWithContentLength =>
            "RFC 9112 §6.3: Both Transfer-Encoding and Content-Length are present; request-smuggling risk.",

        HttpDecodeError.InvalidTrailerHeader =>
            "RFC 9112 §6.5: Invalid trailer header field.",

        HttpDecodeError.InvalidChunkSize =>
            "RFC 9112 §7.1.1: Invalid chunk-size. Expected one or more hexadecimal digits.",

        HttpDecodeError.ChunkDataTruncated =>
            "RFC 9112 §7.1.3: Chunk data is truncated; received fewer bytes than the declared chunk-size.",

        HttpDecodeError.InvalidChunkExtension =>
            "RFC 9112 §7.1.1: Invalid chunk-ext syntax. Expected '; name[=value]' pairs after the chunk-size.",

        HttpDecodeError.TooManyHeaders =>
            "Security (RFC 9112 §5): Header count exceeds the configured maximum; possible header-flood attack.",

        _ => $"HTTP decode error: {error}."
    };
}
