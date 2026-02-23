#nullable enable

using System.Text;
using TurboHttp.Protocol;
using Xunit;

namespace TurboHttp.Tests;

/// <summary>
/// Phase 34 — Error Codes &amp; Messages
/// Verifies that HttpDecoderException carries RFC-referenced, actionable messages
/// for every HttpDecodeError value, and that context overloads append caller context.
/// </summary>
public sealed class HttpDecodeErrorMessagesTests
{
    // ── Default message tests: each code must reference its RFC section ─────────

    [Fact(DisplayName = "34-msg-001: InvalidStatusLine — message contains RFC 9112 §4")]
    public void Should_Include_RfcReference_When_InvalidStatusLine()
    {
        var ex = new HttpDecoderException(HttpDecodeError.InvalidStatusLine);
        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("§4", ex.Message);
    }

    [Fact(DisplayName = "34-msg-002: InvalidHeader — message contains RFC 9112 §5.1")]
    public void Should_Include_RfcReference_When_InvalidHeader()
    {
        var ex = new HttpDecoderException(HttpDecodeError.InvalidHeader);
        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("§5.1", ex.Message);
    }

    [Fact(DisplayName = "34-msg-003: InvalidContentLength — message contains RFC 9112 §6.3")]
    public void Should_Include_RfcReference_When_InvalidContentLength()
    {
        var ex = new HttpDecoderException(HttpDecodeError.InvalidContentLength);
        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("§6.3", ex.Message);
    }

    [Fact(DisplayName = "34-msg-004: InvalidChunkedEncoding — message contains RFC 9112 §7.1")]
    public void Should_Include_RfcReference_When_InvalidChunkedEncoding()
    {
        var ex = new HttpDecoderException(HttpDecodeError.InvalidChunkedEncoding);
        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("§7.1", ex.Message);
    }

    [Fact(DisplayName = "34-msg-005: LineTooLong — message contains RFC 9112 §2.3")]
    public void Should_Include_RfcReference_When_LineTooLong()
    {
        var ex = new HttpDecoderException(HttpDecodeError.LineTooLong);
        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("§2.3", ex.Message);
    }

    [Fact(DisplayName = "34-msg-006: InvalidRequestLine — message contains RFC 9112 §3")]
    public void Should_Include_RfcReference_When_InvalidRequestLine()
    {
        var ex = new HttpDecoderException(HttpDecodeError.InvalidRequestLine);
        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("§3", ex.Message);
    }

    [Fact(DisplayName = "34-msg-007: InvalidMethodToken — message contains RFC 9112 §3.1")]
    public void Should_Include_RfcReference_When_InvalidMethodToken()
    {
        var ex = new HttpDecoderException(HttpDecodeError.InvalidMethodToken);
        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("§3.1", ex.Message);
    }

    [Fact(DisplayName = "34-msg-008: InvalidRequestTarget — message contains RFC 9112 §3.2")]
    public void Should_Include_RfcReference_When_InvalidRequestTarget()
    {
        var ex = new HttpDecoderException(HttpDecodeError.InvalidRequestTarget);
        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("§3.2", ex.Message);
    }

    [Fact(DisplayName = "34-msg-009: InvalidHttpVersion — message contains RFC 9112 §2.3")]
    public void Should_Include_RfcReference_When_InvalidHttpVersion()
    {
        var ex = new HttpDecoderException(HttpDecodeError.InvalidHttpVersion);
        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("§2.3", ex.Message);
    }

    [Fact(DisplayName = "34-msg-010: MissingHostHeader — message contains RFC 9112 §5.4")]
    public void Should_Include_RfcReference_When_MissingHostHeader()
    {
        var ex = new HttpDecoderException(HttpDecodeError.MissingHostHeader);
        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("§5.4", ex.Message);
    }

    [Fact(DisplayName = "34-msg-011: MultipleHostHeaders — message contains RFC 9112 §5.4")]
    public void Should_Include_RfcReference_When_MultipleHostHeaders()
    {
        var ex = new HttpDecoderException(HttpDecodeError.MultipleHostHeaders);
        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("§5.4", ex.Message);
    }

    [Fact(DisplayName = "34-msg-012: MultipleContentLengthValues — message contains RFC 9112 §6.3")]
    public void Should_Include_RfcReference_When_MultipleContentLengthValues()
    {
        var ex = new HttpDecoderException(HttpDecodeError.MultipleContentLengthValues);
        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("§6.3", ex.Message);
    }

    [Fact(DisplayName = "34-msg-013: InvalidFieldName — message contains RFC 9112 §5.1")]
    public void Should_Include_RfcReference_When_InvalidFieldName()
    {
        var ex = new HttpDecoderException(HttpDecodeError.InvalidFieldName);
        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("§5.1", ex.Message);
    }

    [Fact(DisplayName = "34-msg-014: InvalidFieldValue — message contains RFC 9112 §5.5")]
    public void Should_Include_RfcReference_When_InvalidFieldValue()
    {
        var ex = new HttpDecoderException(HttpDecodeError.InvalidFieldValue);
        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("§5.5", ex.Message);
    }

    [Fact(DisplayName = "34-msg-015: ObsoleteFoldingDetected — message contains RFC 9112 §5.2")]
    public void Should_Include_RfcReference_When_ObsoleteFoldingDetected()
    {
        var ex = new HttpDecoderException(HttpDecodeError.ObsoleteFoldingDetected);
        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("§5.2", ex.Message);
    }

    [Fact(DisplayName = "34-msg-016: ChunkedWithContentLength — message contains RFC 9112 §6.3")]
    public void Should_Include_RfcReference_When_ChunkedWithContentLength()
    {
        var ex = new HttpDecoderException(HttpDecodeError.ChunkedWithContentLength);
        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("§6.3", ex.Message);
    }

    [Fact(DisplayName = "34-msg-017: InvalidChunkSize — message contains RFC 9112 §7.1.1")]
    public void Should_Include_RfcReference_When_InvalidChunkSize()
    {
        var ex = new HttpDecoderException(HttpDecodeError.InvalidChunkSize);
        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("§7.1.1", ex.Message);
    }

    [Fact(DisplayName = "34-msg-018: ChunkDataTruncated — message contains RFC 9112 §7.1.3")]
    public void Should_Include_RfcReference_When_ChunkDataTruncated()
    {
        var ex = new HttpDecoderException(HttpDecodeError.ChunkDataTruncated);
        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("§7.1.3", ex.Message);
    }

    [Fact(DisplayName = "34-msg-019: InvalidChunkExtension — message contains RFC 9112 §7.1.1")]
    public void Should_Include_RfcReference_When_InvalidChunkExtension()
    {
        var ex = new HttpDecoderException(HttpDecodeError.InvalidChunkExtension);
        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("§7.1.1", ex.Message);
    }

    [Fact(DisplayName = "34-msg-020: TooManyHeaders — message contains Security note and RFC 9112 §5")]
    public void Should_Include_SecurityNote_When_TooManyHeaders()
    {
        var ex = new HttpDecoderException(HttpDecodeError.TooManyHeaders);
        Assert.Contains("Security", ex.Message);
        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("§5", ex.Message);
    }

    // ── Message quality: must not be just the raw enum name ─────────────────────

    [Fact(DisplayName = "34-msg-021: InvalidStatusLine — message is not just enum name")]
    public void Should_NotBeJustEnumName_When_InvalidStatusLine()
    {
        var ex = new HttpDecoderException(HttpDecodeError.InvalidStatusLine);
        Assert.NotEqual("InvalidStatusLine", ex.Message);
        Assert.True(ex.Message.Length > 20, "Message should be descriptive, not just the enum name.");
    }

    [Fact(DisplayName = "34-msg-022: TooManyHeaders — message is not just enum name")]
    public void Should_NotBeJustEnumName_When_TooManyHeaders()
    {
        var ex = new HttpDecoderException(HttpDecodeError.TooManyHeaders);
        Assert.NotEqual("TooManyHeaders", ex.Message);
        Assert.True(ex.Message.Length > 20);
    }

    // ── Context overload ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "34-msg-023: Context overload — caller context appears in message")]
    public void Should_IncludeContext_When_ContextProvided()
    {
        const string context = "Received 150 fields; limit is 100.";
        var ex = new HttpDecoderException(HttpDecodeError.TooManyHeaders, context);
        Assert.Contains(context, ex.Message);
    }

    [Fact(DisplayName = "34-msg-024: Context overload — default RFC message also present")]
    public void Should_StillIncludeDefaultMessage_When_ContextProvided()
    {
        var ex = new HttpDecoderException(HttpDecodeError.TooManyHeaders, "extra info");
        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("extra info", ex.Message);
    }

    [Fact(DisplayName = "34-msg-025: Context overload — DecodeError property preserved")]
    public void Should_PreserveDecodeError_When_ContextOverloadUsed()
    {
        var ex = new HttpDecoderException(HttpDecodeError.InvalidChunkSize, "Value: 'zz'.");
        Assert.Equal(HttpDecodeError.InvalidChunkSize, ex.DecodeError);
    }

    [Fact(DisplayName = "34-msg-026: Default constructor — DecodeError property correct")]
    public void Should_PreserveDecodeError_When_DefaultConstructorUsed()
    {
        var ex = new HttpDecoderException(HttpDecodeError.InvalidStatusLine);
        Assert.Equal(HttpDecodeError.InvalidStatusLine, ex.DecodeError);
    }

    // ── Exception hierarchy ──────────────────────────────────────────────────────

    [Fact(DisplayName = "34-msg-027: HttpDecoderException inherits from System.Exception")]
    public void Should_InheritFromException()
    {
        var ex = new HttpDecoderException(HttpDecodeError.InvalidStatusLine);
        Assert.IsAssignableFrom<Exception>(ex);
    }

    // ── Integration: Http11Decoder throws with context ───────────────────────────

    [Fact(DisplayName = "34-msg-028: Http11Decoder TooManyHeaders — context includes count and limit")]
    public void Should_IncludeHeaderCount_When_Http11DecoderThrowsTooManyHeaders()
    {
        var decoder = new Http11Decoder(maxHeaderCount: 2);
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 200 OK\r\n");
        sb.Append("X-A: 1\r\n");
        sb.Append("X-B: 2\r\n");
        sb.Append("X-C: 3\r\n"); // exceeds limit of 2
        sb.Append("\r\n");
        var raw = Encoding.ASCII.GetBytes(sb.ToString());

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.TooManyHeaders, ex.DecodeError);
        Assert.Contains("limit is 2", ex.Message);
    }

    [Fact(DisplayName = "34-msg-029: Http11Decoder InvalidFieldValue — context includes field name")]
    public void Should_IncludeFieldName_When_Http11DecoderThrowsInvalidFieldValue()
    {
        var decoder = new Http11Decoder();
        // X-Bad value contains a CR character (0x0D)
        var raw = "HTTP/1.1 200 OK\r\nX-Bad: val\rue\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidFieldValue, ex.DecodeError);
        Assert.Contains("X-Bad", ex.Message);
    }

    [Fact(DisplayName = "34-msg-030: Http11Decoder MultipleContentLengthValues — context includes both values")]
    public void Should_IncludeConflictingValues_When_Http11DecoderThrowsMultipleContentLengths()
    {
        var decoder = new Http11Decoder();
        var raw = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nContent-Length: 10\r\n\r\nHello"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.MultipleContentLengthValues, ex.DecodeError);
        Assert.Contains("5", ex.Message);
        Assert.Contains("10", ex.Message);
    }

    // ── Integration: Http10Decoder throws with context ───────────────────────────

    [Fact(DisplayName = "34-msg-031: Http10Decoder InvalidStatusLine — context includes actual line")]
    public void Should_IncludeStatusLine_When_Http10DecoderThrowsInvalidStatusLine()
    {
        var decoder = new Http10Decoder();
        // A malformed status line with no status code
        var raw = "INVALID\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidStatusLine, ex.DecodeError);
        Assert.Contains("INVALID", ex.Message);
    }

    [Fact(DisplayName = "34-msg-032: Http10Decoder InvalidContentLength — context includes actual value")]
    public void Should_IncludeActualValue_When_Http10DecoderThrowsInvalidContentLength()
    {
        var decoder = new Http10Decoder();
        var raw = "HTTP/1.0 200 OK\r\nContent-Length: notanumber\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidContentLength, ex.DecodeError);
        Assert.Contains("notanumber", ex.Message);
    }

    [Fact(DisplayName = "34-msg-033: Http10Decoder MultipleContentLengthValues — context includes both values")]
    public void Should_IncludeConflictingValues_When_Http10DecoderThrowsMultipleContentLengths()
    {
        var decoder = new Http10Decoder();
        var raw = "HTTP/1.0 200 OK\r\nContent-Length: 5\r\nContent-Length: 10\r\n\r\nHello"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.MultipleContentLengthValues, ex.DecodeError);
        Assert.Contains("5", ex.Message);
        Assert.Contains("10", ex.Message);
    }

    // ── Integration: Http11Decoder chunk size error via decoder ──────────────────

    [Fact(DisplayName = "34-msg-034: Http11Decoder InvalidChunkSize — message contains RFC 9112 §7.1.1")]
    public void Should_Include_RfcSection_When_Http11DecoderThrowsInvalidChunkSize()
    {
        var decoder = new Http11Decoder();
        // Transfer-Encoding: chunked with an invalid hex chunk size "ZZ"
        var raw = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\nZZ\r\nbad\r\n0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidChunkSize, ex.DecodeError);
        Assert.Contains("RFC 9112", ex.Message);
        Assert.Contains("§7.1.1", ex.Message);
    }
}
