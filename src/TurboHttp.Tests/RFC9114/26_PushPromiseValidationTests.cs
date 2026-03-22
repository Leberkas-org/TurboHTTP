using TurboHttp.Protocol.RFC9114;

namespace TurboHttp.Tests.RFC9114;

public sealed class PushPromiseValidationTests
{
    private static readonly List<(string Name, string Value)> ValidHeaders = new()
    {
        (":method", "GET"),
        (":scheme", "https"),
        (":path", "/resource"),
        ("accept", "text/html"),
    };

    private static Http3PushPromiseValidator CreateValidator(long maxPushId = 10)
    {
        var handler = new Http3MaxPushIdHandler();
        handler.CreateMaxPushId(maxPushId);
        return new Http3PushPromiseValidator(handler);
    }

    // ───────────── Push ID range ─────────────

    [Fact(DisplayName = "RFC9114-4.6-PP-001: Push ID within MAX_PUSH_ID limit accepted")]
    public void PushId_WithinLimit_Accepted()
    {
        var validator = CreateValidator(10);
        var frame = new Http3PushPromiseFrame(5, ReadOnlyMemory<byte>.Empty);

        validator.Validate(frame, ValidHeaders);

        Assert.Equal(1, validator.UsedPushIdCount);
        Assert.True(validator.IsPushIdUsed(5));
    }

    [Fact(DisplayName = "RFC9114-4.6-PP-002: Push ID at MAX_PUSH_ID limit accepted")]
    public void PushId_AtLimit_Accepted()
    {
        var validator = CreateValidator(10);
        var frame = new Http3PushPromiseFrame(10, ReadOnlyMemory<byte>.Empty);

        validator.Validate(frame, ValidHeaders);

        Assert.True(validator.IsPushIdUsed(10));
    }

    [Fact(DisplayName = "RFC9114-4.6-PP-003: Push ID zero accepted")]
    public void PushId_Zero_Accepted()
    {
        var validator = CreateValidator(10);
        var frame = new Http3PushPromiseFrame(0, ReadOnlyMemory<byte>.Empty);

        validator.Validate(frame, ValidHeaders);

        Assert.True(validator.IsPushIdUsed(0));
    }

    [Fact(DisplayName = "RFC9114-7.2.7-PP-004: Push ID exceeding MAX_PUSH_ID is H3_ID_ERROR")]
    public void PushId_ExceedsLimit_ThrowsIdError()
    {
        var validator = CreateValidator(10);
        var frame = new Http3PushPromiseFrame(11, ReadOnlyMemory<byte>.Empty);

        var ex = Assert.Throws<Http3Exception>(
            () => validator.Validate(frame, ValidHeaders));
        Assert.Equal(Http3ErrorCode.IdError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-7.2.7-PP-005: Push ID without MAX_PUSH_ID sent is H3_ID_ERROR")]
    public void PushId_NoMaxPushIdSent_ThrowsIdError()
    {
        var handler = new Http3MaxPushIdHandler();
        var validator = new Http3PushPromiseValidator(handler);
        var frame = new Http3PushPromiseFrame(0, ReadOnlyMemory<byte>.Empty);

        var ex = Assert.Throws<Http3Exception>(
            () => validator.Validate(frame, ValidHeaders));
        Assert.Equal(Http3ErrorCode.IdError, ex.ErrorCode);
    }

    // ───────────── Duplicate push ID ─────────────

    [Fact(DisplayName = "RFC9114-4.6-PP-006: Duplicate push ID is H3_ID_ERROR")]
    public void DuplicatePushId_ThrowsIdError()
    {
        var validator = CreateValidator(10);
        var frame1 = new Http3PushPromiseFrame(5, ReadOnlyMemory<byte>.Empty);
        var frame2 = new Http3PushPromiseFrame(5, ReadOnlyMemory<byte>.Empty);

        validator.Validate(frame1, ValidHeaders);

        var ex = Assert.Throws<Http3Exception>(
            () => validator.Validate(frame2, ValidHeaders));
        Assert.Equal(Http3ErrorCode.IdError, ex.ErrorCode);
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(DisplayName = "RFC9114-4.6-PP-007: Different push IDs accepted sequentially")]
    public void DifferentPushIds_AcceptedSequentially()
    {
        var validator = CreateValidator(10);

        validator.Validate(new Http3PushPromiseFrame(0, ReadOnlyMemory<byte>.Empty), ValidHeaders);
        validator.Validate(new Http3PushPromiseFrame(1, ReadOnlyMemory<byte>.Empty), ValidHeaders);
        validator.Validate(new Http3PushPromiseFrame(5, ReadOnlyMemory<byte>.Empty), ValidHeaders);

        Assert.Equal(3, validator.UsedPushIdCount);
        Assert.True(validator.IsPushIdUsed(0));
        Assert.True(validator.IsPushIdUsed(1));
        Assert.True(validator.IsPushIdUsed(5));
        Assert.False(validator.IsPushIdUsed(3));
    }

    [Fact(DisplayName = "RFC9114-4.6-PP-008: Push IDs need not be sequential")]
    public void PushIds_NonSequential_Accepted()
    {
        var validator = CreateValidator(100);

        validator.Validate(new Http3PushPromiseFrame(7, ReadOnlyMemory<byte>.Empty), ValidHeaders);
        validator.Validate(new Http3PushPromiseFrame(3, ReadOnlyMemory<byte>.Empty), ValidHeaders);
        validator.Validate(new Http3PushPromiseFrame(99, ReadOnlyMemory<byte>.Empty), ValidHeaders);

        Assert.Equal(3, validator.UsedPushIdCount);
    }

    // ───────────── Promised request header validation ─────────────

    [Fact(DisplayName = "RFC9114-4.3.1-PP-009: Valid GET push promise accepted")]
    public void ValidGetPushPromise_Accepted()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":scheme", "https"),
            (":path", "/index.html"),
        };

        Http3PushPromiseValidator.ValidatePromisedHeaders(headers);
    }

    [Fact(DisplayName = "RFC9114-4.3.1-PP-010: Valid HEAD push promise accepted")]
    public void ValidHeadPushPromise_Accepted()
    {
        var headers = new List<(string, string)>
        {
            (":method", "HEAD"),
            (":scheme", "https"),
            (":path", "/index.html"),
        };

        Http3PushPromiseValidator.ValidatePromisedHeaders(headers);
    }

    [Fact(DisplayName = "RFC9114-4.3.1-PP-011: Missing :method is H3_MESSAGE_ERROR")]
    public void MissingMethod_ThrowsMessageError()
    {
        var headers = new List<(string, string)>
        {
            (":scheme", "https"),
            (":path", "/resource"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3PushPromiseValidator.ValidatePromisedHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":method", ex.Message);
    }

    [Fact(DisplayName = "RFC9114-4.3.1-PP-012: Missing :scheme is H3_MESSAGE_ERROR")]
    public void MissingScheme_ThrowsMessageError()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/resource"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3PushPromiseValidator.ValidatePromisedHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":scheme", ex.Message);
    }

    [Fact(DisplayName = "RFC9114-4.3.1-PP-013: Missing :path is H3_MESSAGE_ERROR")]
    public void MissingPath_ThrowsMessageError()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":scheme", "https"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3PushPromiseValidator.ValidatePromisedHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":path", ex.Message);
    }

    [Fact(DisplayName = "RFC9114-4.6-PP-014: :status pseudo-header is H3_MESSAGE_ERROR")]
    public void StatusPseudoHeader_ThrowsMessageError()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":scheme", "https"),
            (":path", "/resource"),
            (":status", "200"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3PushPromiseValidator.ValidatePromisedHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":status", ex.Message);
    }

    [Theory(DisplayName = "RFC9114-4.6-PP-015: Unsafe methods rejected in PUSH_PROMISE")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public void UnsafeMethod_ThrowsMessageError(string method)
    {
        var headers = new List<(string, string)>
        {
            (":method", method),
            (":scheme", "https"),
            (":path", "/resource"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3PushPromiseValidator.ValidatePromisedHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("safe and cacheable", ex.Message);
    }

    [Theory(DisplayName = "RFC9114-4.6-PP-016: Safe but not cacheable methods rejected")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public void SafeButNotCacheable_ThrowsMessageError(string method)
    {
        var headers = new List<(string, string)>
        {
            (":method", method),
            (":scheme", "https"),
            (":path", "/resource"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3PushPromiseValidator.ValidatePromisedHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-4.3.1-PP-017: Duplicate :method pseudo-header rejected")]
    public void DuplicateMethod_ThrowsMessageError()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":method", "HEAD"),
            (":scheme", "https"),
            (":path", "/resource"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3PushPromiseValidator.ValidatePromisedHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Duplicate :method", ex.Message);
    }

    [Fact(DisplayName = "RFC9114-4.3.1-PP-018: Duplicate :scheme pseudo-header rejected")]
    public void DuplicateScheme_ThrowsMessageError()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":scheme", "https"),
            (":scheme", "http"),
            (":path", "/resource"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3PushPromiseValidator.ValidatePromisedHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Duplicate :scheme", ex.Message);
    }

    [Fact(DisplayName = "RFC9114-4.3.1-PP-019: Duplicate :path pseudo-header rejected")]
    public void DuplicatePath_ThrowsMessageError()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":scheme", "https"),
            (":path", "/a"),
            (":path", "/b"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3PushPromiseValidator.ValidatePromisedHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Duplicate :path", ex.Message);
    }

    // ───────────── Connection-specific header validation ─────────────

    [Fact(DisplayName = "RFC9114-4.2-PP-020: Connection header forbidden in PUSH_PROMISE")]
    public void ConnectionHeader_ThrowsMessageError()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":scheme", "https"),
            (":path", "/resource"),
            ("connection", "keep-alive"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3PushPromiseValidator.ValidatePromisedHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-4.2-PP-021: Transfer-Encoding header forbidden in PUSH_PROMISE")]
    public void TransferEncodingHeader_ThrowsMessageError()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":scheme", "https"),
            (":path", "/resource"),
            ("transfer-encoding", "chunked"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3PushPromiseValidator.ValidatePromisedHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-4.2-PP-022: Uppercase field name rejected in PUSH_PROMISE")]
    public void UppercaseFieldName_ThrowsMessageError()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":scheme", "https"),
            (":path", "/resource"),
            ("Content-Type", "text/html"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3PushPromiseValidator.ValidatePromisedHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-4.2-PP-023: TE with value 'trailers' allowed in PUSH_PROMISE")]
    public void TeTrailers_Accepted()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":scheme", "https"),
            (":path", "/resource"),
            ("te", "trailers"),
        };

        Http3PushPromiseValidator.ValidatePromisedHeaders(headers);
    }

    [Fact(DisplayName = "RFC9114-4.2-PP-024: TE with non-trailers value rejected")]
    public void TeNonTrailers_ThrowsMessageError()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":scheme", "https"),
            (":path", "/resource"),
            ("te", "gzip"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3PushPromiseValidator.ValidatePromisedHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    // ───────────── Full validation (push ID + headers) ─────────────

    [Fact(DisplayName = "RFC9114-4.6-PP-025: Full validation with valid frame and headers")]
    public void FullValidation_ValidFrameAndHeaders()
    {
        var validator = CreateValidator(10);
        var frame = new Http3PushPromiseFrame(3, ReadOnlyMemory<byte>.Empty);

        validator.Validate(frame, ValidHeaders);

        Assert.True(validator.IsPushIdUsed(3));
    }

    [Fact(DisplayName = "RFC9114-4.6-PP-026: Full validation rejects bad push ID before checking headers")]
    public void FullValidation_BadPushId_ThrowsIdError()
    {
        var validator = CreateValidator(5);
        var frame = new Http3PushPromiseFrame(99, ReadOnlyMemory<byte>.Empty);
        var badHeaders = new List<(string, string)>(); // also invalid

        var ex = Assert.Throws<Http3Exception>(
            () => validator.Validate(frame, badHeaders));
        Assert.Equal(Http3ErrorCode.IdError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-4.6-PP-027: Constructor rejects null handler")]
    public void NullHandler_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Http3PushPromiseValidator(null!));
    }

    [Fact(DisplayName = "RFC9114-4.6-PP-028: Push promise with regular headers alongside pseudo-headers accepted")]
    public void RegularHeaders_Accepted()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":scheme", "https"),
            (":path", "/resource"),
            ("accept", "text/html"),
            ("accept-language", "en-US"),
            ("cache-control", "no-cache"),
        };

        Http3PushPromiseValidator.ValidatePromisedHeaders(headers);
    }

    [Fact(DisplayName = "RFC9114-4.6-PP-029: Empty :path value is accepted (validation is structural only)")]
    public void EmptyPathValue_Accepted()
    {
        // The validator checks for presence of :path, not its value validity
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":scheme", "https"),
            (":path", ""),
        };

        Http3PushPromiseValidator.ValidatePromisedHeaders(headers);
    }

    [Fact(DisplayName = "RFC9114-4.6-PP-030: Large push ID within range accepted")]
    public void LargePushId_WithinRange_Accepted()
    {
        var maxId = 4611686018427387903L; // 2^62 - 1
        var validator = CreateValidator(maxId);
        var frame = new Http3PushPromiseFrame(maxId, ReadOnlyMemory<byte>.Empty);

        validator.Validate(frame, ValidHeaders);

        Assert.True(validator.IsPushIdUsed(maxId));
    }
}
