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
    public HttpDecoderError DecodeError { get; }

    /// <summary>Creates an exception for the given error code with a default RFC-referenced message.</summary>
    public HttpDecoderException(HttpDecoderError error) : base(GetDefaultMessage(error))
    {
        DecodeError = error;
    }

    /// <summary>
    /// Creates an exception for the given error code, appending caller-supplied context
    /// (e.g. "Received 150 fields; limit is 100.") to the default RFC-referenced message.
    /// </summary>
    public HttpDecoderException(HttpDecoderError error, string context) : base($"{GetDefaultMessage(error)} {context}")
    {
        DecodeError = error;
    }

    /// <summary>
    /// Returns the default human-readable message for <paramref name="error"/>,
    /// including the relevant RFC section reference.
    /// </summary>
    internal static string GetDefaultMessage(HttpDecoderError error) => error switch
    {
        HttpDecoderError.NeedMoreData
            => "More data required to complete parsing.",

        HttpDecoderError.InvalidStatusLine
            => @"RFC 9112 §4: Invalid status-line. Expected 'HTTP/1.x NNN reason-phrase\r\n'.",

        HttpDecoderError.InvalidHeader
            => @"RFC 9112 §5.1: Invalid header field. Expected 'name: value\r\n'; missing or misplaced colon separator.",

        HttpDecoderError.InvalidContentLength
            => "RFC 9112 §6.3: Invalid Content-Length value. Must be a non-negative integer.",

        HttpDecoderError.InvalidChunkedEncoding
            => "RFC 9112 §7.1: Invalid chunked transfer-encoding format.",

        HttpDecoderError.DecompressionFailed
            => "Content decompression failed.",

        HttpDecoderError.LineTooLong
            => "RFC 9112 §5.4: Line length exceeds the configured maximum.",

        HttpDecoderError.InvalidRequestLine
            => @"RFC 9112 §3: Invalid request-line. Expected 'METHOD SP request-target SP HTTP/1.x\r\n'.",

        HttpDecoderError.InvalidMethodToken
            => "RFC 9112 §3.1: Invalid HTTP method token. Methods must consist of token characters only.",

        HttpDecoderError.InvalidRequestTarget
            => "RFC 9112 §3.2: Invalid request-target.",

        HttpDecoderError.InvalidHttpVersion
            => "RFC 9112 §2.3: Invalid HTTP version. Expected 'HTTP/1.0' or 'HTTP/1.1'.",

        HttpDecoderError.MissingHostHeader
            => "RFC 9110 §7.2: Missing required Host header in HTTP/1.1 request.",

        HttpDecoderError.MultipleHostHeaders
            => "RFC 9110 §7.2: Multiple Host headers present; exactly one is required.",

        HttpDecoderError.MultipleContentLengthValues
            => "RFC 9112 §6.3: Multiple Content-Length headers with conflicting values; request-smuggling risk.",

        HttpDecoderError.InvalidFieldName
            => "RFC 9112 §5.1: Invalid header field name. Names must be token characters with no surrounding whitespace.",

        HttpDecoderError.InvalidFieldValue
            => @"RFC 9112 §5.5: Invalid header field value. Values must not contain CR (\r), LF (\n), or NUL (\0) bytes.",

        HttpDecoderError.ObsoleteFoldingDetected
            => "RFC 9112 §5.2: Obsolete line folding detected. Folded header values are not permitted.",

        HttpDecoderError.ChunkedWithContentLength
            => "RFC 9112 §6.3: Both Transfer-Encoding and Content-Length are present; request-smuggling risk.",

        HttpDecoderError.InvalidTrailerHeader
            => "RFC 9112 §7.1.2: Invalid trailer header field.",

        HttpDecoderError.InvalidChunkSize
            => "RFC 9112 §7.1.1: Invalid chunk-size. Expected one or more hexadecimal digits.",

        HttpDecoderError.ChunkDataTruncated
            => "RFC 9112 §7.1.3: Chunk data is truncated; received fewer bytes than the declared chunk-size.",

        HttpDecoderError.InvalidChunkExtension
            => "RFC 9112 §7.1.1: Invalid chunk-ext syntax. Expected '; name[=value]' pairs after the chunk-size.",

        HttpDecoderError.TooManyHeaders
            => "Security (RFC 9112 §5): Header count exceeds the configured maximum; possible header-flood attack.",

        _ => $"HTTP decode error: {error}."
    };
}