using System.Net;
using TurboHttp.Protocol.Http3;
using TurboHttp.Protocol.Http3.Qpack;

namespace TurboHttp.Tests.Http3.Streams;

/// <summary>
/// RFC 9114 §4.1, §4.3.2 — Http3ResponseDecoder validation and error-handling tests.
/// Covers missing/duplicate/unknown/ordered pseudo-headers, status range validation,
/// empty/null frame guards, trailing headers, stream ID forwarding, content headers,
/// pseudo-header exclusion from response.Headers, and ValidateResponsePseudoHeaders.
/// </summary>
public sealed class Http3ResponseDecoderValidationSpec
{
    private static Http3HeadersFrame EncodeHeaders(
        IReadOnlyList<(string Name, string Value)> headers,
        int maxTableCapacity = 0)
    {
        var encoder = new QpackEncoder(maxTableCapacity);
        var block = encoder.Encode(headers);
        return new Http3HeadersFrame(block);
    }

    private static Http3HeadersFrame EncodeResponseHeaders(
        int statusCode,
        IReadOnlyList<(string Name, string Value)>? extraHeaders = null,
        int maxTableCapacity = 0)
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", statusCode.ToString()),
        };
        if (extraHeaders is not null)
        {
            headers.AddRange(extraHeaders);
        }

        return EncodeHeaders(headers, maxTableCapacity);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void Missing_status_throws()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeHeaders(new[]
        {
            ("server", "test"),
        });

        var ex = Assert.Throws<Http3Exception>(
            () => decoder.Decode(new[] { headersFrame }));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void Duplicate_status_throws()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeHeaders(new[]
        {
            (":status", "200"),
            (":status", "404"),
        });

        var ex = Assert.Throws<Http3Exception>(
            () => decoder.Decode(new[] { headersFrame }));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void Unknown_pseudo_header_throws()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeHeaders(new[]
        {
            (":status", "200"),
            (":method", "GET"),
        });

        var ex = Assert.Throws<Http3Exception>(
            () => decoder.Decode(new[] { headersFrame }));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Unknown", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void Pseudo_after_regular_throws()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeHeaders(new[]
        {
            ("server", "test"),
            (":status", "200"),
        });

        var ex = Assert.Throws<Http3Exception>(
            () => decoder.Decode(new[] { headersFrame }));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void Invalid_status_value_throws()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeHeaders(new[]
        {
            (":status", "abc"),
        });

        var ex = Assert.Throws<Http3Exception>(
            () => decoder.Decode(new[] { headersFrame }));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void Status_below_100_throws()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeHeaders(new[]
        {
            (":status", "99"),
        });

        var ex = Assert.Throws<Http3Exception>(
            () => decoder.Decode(new[] { headersFrame }));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void Status_above_999_throws()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeHeaders(new[]
        {
            (":status", "1000"),
        });

        var ex = Assert.Throws<Http3Exception>(
            () => decoder.Decode(new[] { headersFrame }));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Empty_frames_throws()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);

        var ex = Assert.Throws<Http3Exception>(
            () => decoder.Decode(Array.Empty<Http3Frame>()));
        Assert.Equal(Http3ErrorCode.FrameUnexpected, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Null_frames_throws()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        Assert.Throws<ArgumentNullException>(() => decoder.Decode(null!));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Data_before_headers_throws()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var dataFrame = new Http3DataFrame("data"u8.ToArray());

        var ex = Assert.Throws<Http3Exception>(
            () => decoder.Decode(new Http3Frame[] { dataFrame }));
        Assert.Equal(Http3ErrorCode.FrameUnexpected, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Null_headers_frame_throws()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        Assert.Throws<ArgumentNullException>(() => decoder.DecodeHeaders(null!));
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Trailing_headers_stops_body()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200);
        var body = "body-data"u8.ToArray();
        var trailingHeaders = EncodeHeaders(new[] { ("x-checksum", "abc") });

        var response = decoder.Decode(new Http3Frame[]
        {
            headersFrame,
            new Http3DataFrame(body),
            trailingHeaders,
        });

        var content = await response.Content!.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(body, content);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Stream_id_passed_to_qpack()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200);

        // Should not throw — streamId is forwarded to QPACK
        var response = decoder.Decode(new[] { headersFrame }, streamId: 42);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }


    [Theory]
    [Trait("RFC", "RFC9114-4.1")]
    [InlineData("content-language", "en-US")]
    [InlineData("content-location", "/resource")]
    [InlineData("content-disposition", "attachment")]
    [InlineData("content-range", "bytes 0-499/1234")]
    [InlineData("expires", "Thu, 01 Dec 2025 16:00:00 GMT")]
    [InlineData("last-modified", "Wed, 09 Oct 2024 10:00:00 GMT")]
    public void Content_headers_decoded(string headerName, string headerValue)
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200, new[]
        {
            (headerName, headerValue),
        });

        var headers = decoder.DecodeHeaders(headersFrame);
        Assert.Contains(headers, h => h.Name == headerName && h.Value == headerValue);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Non_content_headers_stay_on_response()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200, new[]
        {
            ("server", "TurboHttp"),
            ("x-powered-by", "Akka"),
        });

        var response = decoder.Decode(new[] { headersFrame });

        Assert.Contains("TurboHttp", response.Headers.GetValues("server"));
        Assert.Contains("Akka", response.Headers.GetValues("x-powered-by"));
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Pseudo_headers_not_in_response_headers()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200);

        var response = decoder.Decode(new[] { headersFrame });

        Assert.False(response.Headers.Contains(":status"),
            ":status pseudo-header should not appear in response.Headers");
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void Validate_accepts_valid_response()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("server", "test"),
        };

        // Should not throw
        Http3ResponseDecoder.ValidateResponsePseudoHeaders(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void Validate_rejects_request_pseudo_in_response()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (":path", "/"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3ResponseDecoder.ValidateResponsePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }
}
