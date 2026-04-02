using TurboHttp.Protocol.Http3;

namespace TurboHttp.Tests.Http3.Connection;

public sealed class PushPromiseValidationSpec
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


    [Fact]
    [Trait("RFC", "RFC9114-4.6")]
    public void PushId_WithinLimit_Accepted()
    {
        var validator = CreateValidator(10);
        var frame = new Http3PushPromiseFrame(5, ReadOnlyMemory<byte>.Empty);

        validator.Validate(frame, ValidHeaders);

        Assert.Equal(1, validator.UsedPushIdCount);
        Assert.True(validator.IsPushIdUsed(5));
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.6")]
    public void PushId_AtLimit_Accepted()
    {
        var validator = CreateValidator(10);
        var frame = new Http3PushPromiseFrame(10, ReadOnlyMemory<byte>.Empty);

        validator.Validate(frame, ValidHeaders);

        Assert.True(validator.IsPushIdUsed(10));
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.6")]
    public void PushId_Zero_Accepted()
    {
        var validator = CreateValidator(10);
        var frame = new Http3PushPromiseFrame(0, ReadOnlyMemory<byte>.Empty);

        validator.Validate(frame, ValidHeaders);

        Assert.True(validator.IsPushIdUsed(0));
    }

    [Fact]
    [Trait("RFC", "RFC9114-7.2.7")]
    public void PushId_ExceedsLimit_ThrowsIdError()
    {
        var validator = CreateValidator(10);
        var frame = new Http3PushPromiseFrame(11, ReadOnlyMemory<byte>.Empty);

        var ex = Assert.Throws<Http3Exception>(
            () => validator.Validate(frame, ValidHeaders));
        Assert.Equal(Http3ErrorCode.IdError, ex.ErrorCode);
    }

    [Fact]
    [Trait("RFC", "RFC9114-7.2.7")]
    public void PushId_NoMaxPushIdSent_ThrowsIdError()
    {
        var handler = new Http3MaxPushIdHandler();
        var validator = new Http3PushPromiseValidator(handler);
        var frame = new Http3PushPromiseFrame(0, ReadOnlyMemory<byte>.Empty);

        var ex = Assert.Throws<Http3Exception>(
            () => validator.Validate(frame, ValidHeaders));
        Assert.Equal(Http3ErrorCode.IdError, ex.ErrorCode);
    }


    [Fact]
    [Trait("RFC", "RFC9114-4.6")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.6")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.6")]
    public void PushIds_NonSequential_Accepted()
    {
        var validator = CreateValidator(100);

        validator.Validate(new Http3PushPromiseFrame(7, ReadOnlyMemory<byte>.Empty), ValidHeaders);
        validator.Validate(new Http3PushPromiseFrame(3, ReadOnlyMemory<byte>.Empty), ValidHeaders);
        validator.Validate(new Http3PushPromiseFrame(99, ReadOnlyMemory<byte>.Empty), ValidHeaders);

        Assert.Equal(3, validator.UsedPushIdCount);
    }


    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.6")]
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

    [Theory]
    [Trait("RFC", "RFC9114-4.6")]
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

    [Theory]
    [Trait("RFC", "RFC9114-4.6")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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


    [Fact]
    [Trait("RFC", "RFC9114-4.2")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.2")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.2")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.2")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.2")]
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


    [Fact]
    [Trait("RFC", "RFC9114-4.6")]
    public void FullValidation_ValidFrameAndHeaders()
    {
        var validator = CreateValidator(10);
        var frame = new Http3PushPromiseFrame(3, ReadOnlyMemory<byte>.Empty);

        validator.Validate(frame, ValidHeaders);

        Assert.True(validator.IsPushIdUsed(3));
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.6")]
    public void FullValidation_BadPushId_ThrowsIdError()
    {
        var validator = CreateValidator(5);
        var frame = new Http3PushPromiseFrame(99, ReadOnlyMemory<byte>.Empty);
        var badHeaders = new List<(string, string)>(); // also invalid

        var ex = Assert.Throws<Http3Exception>(
            () => validator.Validate(frame, badHeaders));
        Assert.Equal(Http3ErrorCode.IdError, ex.ErrorCode);
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.6")]
    public void NullHandler_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Http3PushPromiseValidator(null!));
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.6")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.6")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.6")]
    public void LargePushId_WithinRange_Accepted()
    {
        var maxId = 4611686018427387903L; // 2^62 - 1
        var validator = CreateValidator(maxId);
        var frame = new Http3PushPromiseFrame(maxId, ReadOnlyMemory<byte>.Empty);

        validator.Validate(frame, ValidHeaders);

        Assert.True(validator.IsPushIdUsed(maxId));
    }
}
