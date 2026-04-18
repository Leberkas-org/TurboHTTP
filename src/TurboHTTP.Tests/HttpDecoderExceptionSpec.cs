using TurboHTTP.Protocol;

namespace TurboHTTP.Tests;

public sealed class HttpDecoderExceptionSpec
{
    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_construct_with_error_code_only()
    {
        var ex = new HttpDecoderException(HttpDecoderError.NeedMoreData);

        Assert.NotNull(ex);
        Assert.Equal(HttpDecoderError.NeedMoreData, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_contain_rfc_reference_in_message()
    {
        var ex = new HttpDecoderException(HttpDecoderError.InvalidStatusLine);

        Assert.Contains("RFC 9112", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_construct_with_error_code_and_context()
    {
        const string context = "Received 150 fields; limit is 100";
        var ex = new HttpDecoderException(HttpDecoderError.TooManyHeaders, context);

        Assert.NotNull(ex);
        Assert.Equal(HttpDecoderError.TooManyHeaders, ex.DecodeError);
        Assert.Contains(context, ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_NeedMoreData()
    {
        var ex = new HttpDecoderException(HttpDecoderError.NeedMoreData);

        Assert.Contains("More data required", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_InvalidStatusLine()
    {
        var ex = new HttpDecoderException(HttpDecoderError.InvalidStatusLine);

        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("status-line", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_InvalidRequestLine()
    {
        var ex = new HttpDecoderException(HttpDecoderError.InvalidRequestLine);

        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("request-line", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_InvalidMethodToken()
    {
        var ex = new HttpDecoderException(HttpDecoderError.InvalidMethodToken);

        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("HTTP method", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_InvalidRequestTarget()
    {
        var ex = new HttpDecoderException(HttpDecoderError.InvalidRequestTarget);

        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("request-target", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_InvalidHttpVersion()
    {
        var ex = new HttpDecoderException(HttpDecoderError.InvalidHttpVersion);

        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("HTTP version", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_InvalidHeader()
    {
        var ex = new HttpDecoderException(HttpDecoderError.InvalidHeader);

        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("header field", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_InvalidFieldName()
    {
        var ex = new HttpDecoderException(HttpDecoderError.InvalidFieldName);

        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("header field name", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_InvalidFieldValue()
    {
        var ex = new HttpDecoderException(HttpDecoderError.InvalidFieldValue);

        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("header field value", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_LineTooLong()
    {
        var ex = new HttpDecoderException(HttpDecoderError.LineTooLong);

        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("Line length", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_ObsoleteFoldingDetected()
    {
        var ex = new HttpDecoderException(HttpDecoderError.ObsoleteFoldingDetected);

        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("line folding", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_InvalidContentLength()
    {
        var ex = new HttpDecoderException(HttpDecoderError.InvalidContentLength);

        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("Content-Length", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_MultipleContentLengthValues()
    {
        var ex = new HttpDecoderException(HttpDecoderError.MultipleContentLengthValues);

        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("Content-Length", ex.Message);
        Assert.Contains("request-smuggling", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_ChunkedWithContentLength()
    {
        var ex = new HttpDecoderException(HttpDecoderError.ChunkedWithContentLength);

        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("Transfer-Encoding", ex.Message);
        Assert.Contains("Content-Length", ex.Message);
        Assert.Contains("request-smuggling", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_InvalidChunkedEncoding()
    {
        var ex = new HttpDecoderException(HttpDecoderError.InvalidChunkedEncoding);

        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("chunked", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_InvalidChunkSize()
    {
        var ex = new HttpDecoderException(HttpDecoderError.InvalidChunkSize);

        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("chunk-size", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_ChunkDataTruncated()
    {
        var ex = new HttpDecoderException(HttpDecoderError.ChunkDataTruncated);

        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("Chunk data", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_InvalidChunkExtension()
    {
        var ex = new HttpDecoderException(HttpDecoderError.InvalidChunkExtension);

        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("chunk-ext", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_InvalidTrailerHeader()
    {
        var ex = new HttpDecoderException(HttpDecoderError.InvalidTrailerHeader);

        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("trailer", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_MissingHostHeader()
    {
        var ex = new HttpDecoderException(HttpDecoderError.MissingHostHeader);

        Assert.Contains("RFC 9110", ex.Message);
        Assert.Contains("Host", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_MultipleHostHeaders()
    {
        var ex = new HttpDecoderException(HttpDecoderError.MultipleHostHeaders);

        Assert.Contains("RFC 9110", ex.Message);
        Assert.Contains("Host", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_DecompressionFailed()
    {
        var ex = new HttpDecoderException(HttpDecoderError.DecompressionFailed);

        Assert.Contains("decompression", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_TooManyHeaders()
    {
        var ex = new HttpDecoderException(HttpDecoderError.TooManyHeaders);

        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("header-flood", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_HeaderTooLarge()
    {
        var ex = new HttpDecoderException(HttpDecoderError.HeaderTooLarge);

        Assert.Contains("header field", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_format_TotalHeadersTooLarge()
    {
        var ex = new HttpDecoderException(HttpDecoderError.TotalHeadersTooLarge);

        Assert.Contains("header", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_append_context_after_default_message()
    {
        const string context = "Expected: HTTP/1.1 200 OK";
        var ex = new HttpDecoderException(HttpDecoderError.InvalidStatusLine, context);

        Assert.Contains("status-line", ex.Message);
        Assert.Contains(context, ex.Message);
        var indexOfStatusLine = ex.Message.IndexOf("status-line", StringComparison.Ordinal);
        var indexOfContext = ex.Message.IndexOf(context, StringComparison.Ordinal);
        Assert.True(indexOfStatusLine < indexOfContext, "Context should appear after default message");
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_preserve_context_with_special_characters()
    {
        const string context = "Line: 'X-Custom: value with \"quotes\" and \\ backslash'";
        var ex = new HttpDecoderException(HttpDecoderError.InvalidHeader, context);

        Assert.Contains(context, ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_handle_empty_context()
    {
        var ex = new HttpDecoderException(HttpDecoderError.InvalidHeader, "");

        Assert.NotEmpty(ex.Message);
        Assert.EndsWith(" ", ex.Message); // Default message + space + empty context
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_inherit_from_TurboProtocolException()
    {
        var ex = new HttpDecoderException(HttpDecoderError.NeedMoreData);

        Assert.IsAssignableFrom<TurboProtocolException>(ex);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_set_decode_error_property()
    {
        const HttpDecoderError error = HttpDecoderError.InvalidChunkedEncoding;
        var ex = new HttpDecoderException(error);

        Assert.Equal(error, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    public void HttpDecoderException_should_set_decode_error_property_with_context()
    {
        const HttpDecoderError error = HttpDecoderError.LineTooLong;
        var ex = new HttpDecoderException(error, "Line is 8192 bytes");

        Assert.Equal(error, ex.DecodeError);
    }

    // Ensure all error codes are handled

    [Theory(Timeout = 5000)]
    [InlineData(0)] // NeedMoreData
    [InlineData(1)] // InvalidStatusLine
    [InlineData(2)] // InvalidHeader
    [InlineData(3)] // InvalidContentLength
    [InlineData(4)] // InvalidChunkedEncoding
    [InlineData(5)] // DecompressionFailed
    [InlineData(6)] // LineTooLong
    [InlineData(7)] // InvalidRequestLine
    [InlineData(8)] // InvalidMethodToken
    [InlineData(9)] // InvalidRequestTarget
    [InlineData(10)] // InvalidHttpVersion
    [InlineData(11)] // MissingHostHeader
    [InlineData(12)] // MultipleHostHeaders
    [InlineData(13)] // MultipleContentLengthValues
    [InlineData(14)] // InvalidFieldName
    [InlineData(15)] // InvalidFieldValue
    [InlineData(16)] // ObsoleteFoldingDetected
    [InlineData(17)] // ChunkedWithContentLength
    [InlineData(18)] // InvalidTrailerHeader
    [InlineData(19)] // InvalidChunkSize
    [InlineData(20)] // ChunkDataTruncated
    [InlineData(21)] // InvalidChunkExtension
    [InlineData(22)] // TooManyHeaders
    [InlineData(23)] // HeaderTooLarge
    [InlineData(24)] // TotalHeadersTooLarge
    public void HttpDecoderException_should_generate_non_empty_message_for_all_error_codes(int errorCode)
    {
        var error = (HttpDecoderError)errorCode;
        var ex = new HttpDecoderException(error);

        Assert.NotEmpty(ex.Message);
        Assert.NotNull(ex.Message);
    }

    [Theory(Timeout = 5000)]
    [InlineData(0)] // NeedMoreData
    [InlineData(1)] // InvalidStatusLine
    [InlineData(2)] // InvalidHeader
    [InlineData(3)] // InvalidContentLength
    [InlineData(4)] // InvalidChunkedEncoding
    [InlineData(5)] // DecompressionFailed
    [InlineData(6)] // LineTooLong
    [InlineData(7)] // InvalidRequestLine
    [InlineData(8)] // InvalidMethodToken
    [InlineData(9)] // InvalidRequestTarget
    [InlineData(10)] // InvalidHttpVersion
    [InlineData(11)] // MissingHostHeader
    [InlineData(12)] // MultipleHostHeaders
    [InlineData(13)] // MultipleContentLengthValues
    [InlineData(14)] // InvalidFieldName
    [InlineData(15)] // InvalidFieldValue
    [InlineData(16)] // ObsoleteFoldingDetected
    [InlineData(17)] // ChunkedWithContentLength
    [InlineData(18)] // InvalidTrailerHeader
    [InlineData(19)] // InvalidChunkSize
    [InlineData(20)] // ChunkDataTruncated
    [InlineData(21)] // InvalidChunkExtension
    [InlineData(22)] // TooManyHeaders
    [InlineData(23)] // HeaderTooLarge
    [InlineData(24)] // TotalHeadersTooLarge
    public void HttpDecoderException_should_preserve_decode_error_for_all_codes(int errorCode)
    {
        var error = (HttpDecoderError)errorCode;
        var ex = new HttpDecoderException(error);

        Assert.Equal(error, ex.DecodeError);
    }
}