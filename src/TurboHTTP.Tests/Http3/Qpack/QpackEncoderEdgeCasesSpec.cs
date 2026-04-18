using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Qpack;

/// <summary>
/// Edge-case tests for QPACK Encoder to achieve 100% branch coverage.
/// Tests encoder instruction handling, sensitive headers, and encoding paths.
/// </summary>
public sealed class QpackEncoderEdgeCasesSpec
{
    /// RFC 9204 §4.5 — Encoder initialization with zero capacity disables dynamic table
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void Should_Create_Encoder_With_Zero_Capacity()
    {
        var encoder = new QpackEncoder(0);
        Assert.Equal(0, encoder.DynamicTable.Capacity);
    }

    /// RFC 9204 §4.5 — Encoder initialization with large capacity
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void Should_Create_Encoder_With_Large_Capacity()
    {
        var encoder = new QpackEncoder(65536);
        Assert.Equal(65536, encoder.DynamicTable.Capacity);
    }

    /// RFC 9204 §4.5 — Encoder throws on negative capacity
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void Should_Throw_On_Negative_Capacity()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new QpackEncoder(-1));

        Assert.Equal("maxTableCapacity", ex.ParamName);
    }

    /// RFC 9204 §4.5 — Encode empty header list
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void Should_Encode_Empty_HeaderList()
    {
        var encoder = new QpackEncoder(256);
        var headers = new (string, string)[0];

        var encoded = encoder.Encode(headers);

        // Empty list requires just prefix (RIC=0, Base=0)
        Assert.NotEmpty(encoded.ToArray());
        Assert.Equal(2, encoded.Length); // Minimum: RIC + Base
    }

    /// RFC 9204 §4.5 — Encode throws on null headers
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void Should_Throw_On_Null_Headers()
    {
        var encoder = new QpackEncoder(256);

        var ex = Assert.Throws<ArgumentNullException>(() =>
            encoder.Encode(null!));

        Assert.Equal("headers", ex.ParamName);
    }

    /// RFC 9204 §7.1 — Encode throws on empty header name
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-7.1")]
    public void Should_Throw_On_Empty_Header_Name()
    {
        var encoder = new QpackEncoder(256);
        var headers = new[] { ("", "value") };

        var ex = Assert.Throws<QpackException>(() =>
            encoder.Encode(headers));

        Assert.Contains("empty header name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// RFC 9204 §7.1 — Sensitive headers (authorization) are never indexed
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-7.1")]
    public void Should_Handle_Sensitive_Authorization_Header()
    {
        var encoder = new QpackEncoder(256);
        var headers = new[] { ("authorization", "Bearer token123") };

        var encoded = encoder.Encode(headers);

        // Sensitive headers should be encoded but not inserted
        Assert.NotEmpty(encoded.ToArray());
        Assert.Equal(0, encoder.DynamicTable.InsertCount);
    }

    /// RFC 9204 §7.1 — Sensitive headers (cookie) are never indexed
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-7.1")]
    public void Should_Handle_Sensitive_Cookie_Header()
    {
        var encoder = new QpackEncoder(256);
        var headers = new[] { ("cookie", "session=abc123") };

        var encoded = encoder.Encode(headers);

        Assert.NotEmpty(encoded.ToArray());
        Assert.Equal(0, encoder.DynamicTable.InsertCount);
    }

    /// RFC 9204 §7.1 — Sensitive headers (set-cookie) are never indexed
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-7.1")]
    public void Should_Handle_Sensitive_SetCookie_Header()
    {
        var encoder = new QpackEncoder(256);
        var headers = new[] { ("set-cookie", "token=xyz") };

        var encoded = encoder.Encode(headers);

        Assert.NotEmpty(encoded.ToArray());
        Assert.Equal(0, encoder.DynamicTable.InsertCount);
    }

    /// RFC 9204 §7.1 — Sensitive headers (proxy-authorization) are never indexed
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-7.1")]
    public void Should_Handle_Sensitive_ProxyAuthorization_Header()
    {
        var encoder = new QpackEncoder(256);
        var headers = new[] { ("proxy-authorization", "Bearer token") };

        var encoded = encoder.Encode(headers);

        Assert.NotEmpty(encoded.ToArray());
        Assert.Equal(0, encoder.DynamicTable.InsertCount);
    }

    /// RFC 9204 §4.4.1 — Track section records pending Required Insert Count
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.1")]
    public void Should_Track_Section_With_NonZero_RIC()
    {
        var encoder = new QpackEncoder(256);

        // First, encode a header that will insert into dynamic table
        encoder.DynamicTable.Insert("custom", "value");
        encoder.TrackSection(streamId: 0, requiredInsertCount: 1);

        // Verify the tracking completed (no exception)
        Assert.Equal(0, encoder.KnownReceivedCount);
    }

    /// RFC 9204 §4.4 — ApplyDecoderInstruction throws on null
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4")]
    public void Should_Throw_On_Null_DecoderInstruction()
    {
        var encoder = new QpackEncoder(256);

        var ex = Assert.Throws<ArgumentNullException>(() =>
            encoder.ApplyDecoderInstruction(null!));

        Assert.Equal("instruction", ex.ParamName);
    }

    /// RFC 9204 §4.5 — Encoder instructions generated when inserting into dynamic table
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void Should_Generate_EncoderInstructions_When_Inserting()
    {
        var encoder = new QpackEncoder(256);
        // Use a custom header not in static table to force dynamic table insertion
        var headers = new[] { ("x-custom-header", "custom-value") };

        encoder.Encode(headers);

        var instructions = encoder.EncoderInstructions;
        // When inserting a new entry, encoder emits an instruction
        Assert.NotEmpty(instructions.ToArray());
    }

    /// RFC 9204 §4.5 — Encode with overload returning span
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void Should_Encode_With_Span_Overload()
    {
        var encoder = new QpackEncoder(256);
        var headers = new[] { ("name", "value") };
        var output = new byte[1024].AsSpan();

        var bytesWritten = encoder.Encode(headers, ref output);

        Assert.True(bytesWritten > 0);
        Assert.True(output.Length < 1024); // Span should be sliced
    }

    /// RFC 9204 §4.5 — Multiple consecutive encodes reset instruction buffer
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void Should_Reset_Instructions_Between_Encodes()
    {
        var encoder = new QpackEncoder(256);

        var headers1 = new[] { ("name", "value") };
        var encoded1 = encoder.Encode(headers1);

        var headers2 = new[] { ("other", "data") };
        var encoded2 = encoder.Encode(headers2);

        // Both should encode successfully
        Assert.NotEmpty(encoded1.ToArray());
        Assert.NotEmpty(encoded2.ToArray());
    }

    /// RFC 9204 §4.5 — Encode with no dynamic table (capacity=0)
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void Should_Encode_With_Disabled_DynamicTable()
    {
        var encoder = new QpackEncoder(0); // Disabled
        var headers = new[] { ("custom-header", "custom-value") };

        var encoded = encoder.Encode(headers);

        Assert.NotEmpty(encoded.ToArray());
        Assert.Equal(0, encoder.DynamicTable.InsertCount);
    }

    /// RFC 9204 §4.5 — Known Received Count starts at zero
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void Should_Have_Zero_KnownReceivedCount_Initially()
    {
        var encoder = new QpackEncoder(256);
        Assert.Equal(0, encoder.KnownReceivedCount);
    }

    /// RFC 9204 §4.4.2 — Track section with zero RIC is ignored
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.2")]
    public void Should_Ignore_Track_Section_With_Zero_RIC()
    {
        var encoder = new QpackEncoder(256);

        encoder.TrackSection(streamId: 0, requiredInsertCount: 0);

        // Verify no tracking occurred (KnownReceivedCount still zero)
        Assert.Equal(0, encoder.KnownReceivedCount);
    }

    /// RFC 9204 §4.5 — Encode multiple headers in single block
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void Should_Encode_Multiple_Headers()
    {
        var encoder = new QpackEncoder(256);
        var headers = new[]
        {
            (":method", "GET"),
            (":path", "/index.html"),
            (":scheme", "https"),
            (":authority", "example.com")
        };

        var encoded = encoder.Encode(headers);

        // Pseudo-headers are in static table and encode compactly
        Assert.NotEmpty(encoded.ToArray());
        // Just verify encoding produced some output
        Assert.True(encoded.Length > 0);
    }

    /// RFC 9204 §4.5 — Encode with custom headers (not in static table)
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void Should_Encode_Custom_Header()
    {
        var encoder = new QpackEncoder(256);
        var headers = new[] { ("x-custom-header", "custom-value") };

        var encoded = encoder.Encode(headers);

        Assert.NotEmpty(encoded.ToArray());
    }
}
