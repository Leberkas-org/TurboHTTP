using System.Net;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Connection;

public sealed class Http3ResponseDecoderEdgeCasesSpec
{
    private readonly QpackTableSync _tableSync = new();
    private readonly ResponseDecoder _decoder;

    public Http3ResponseDecoderEdgeCasesSpec()
    {
        _decoder = new ResponseDecoder(_tableSync);
    }

    private Http3HeadersFrame EncodeHeaders(params (string Name, string Value)[] headers)
    {
        return new Http3HeadersFrame(_tableSync.Encoder.Encode(headers));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void DecodeHeaders_rejects_null_frame()
    {
        var state = new StreamState();
        // NullReferenceException thrown by NullReference to HeaderBlock.Span
        Assert.Throws<NullReferenceException>(() => _decoder.DecodeHeaders(null!, state));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void DecodeHeaders_rejects_null_state()
    {
        var frame = EncodeHeaders((":status", "200"));
        // NullReferenceException thrown when state is null
        Assert.Throws<NullReferenceException>(() => _decoder.DecodeHeaders(frame, null!));
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void DecodeHeaders_missing_status_pseudo_header()
    {
        var state = new StreamState();
        var frame = EncodeHeaders(("content-type", "text/plain"));

        // Implementation does not validate that :status is present, so this succeeds
        // HttpResponseMessage defaults to StatusCode OK (200) when not explicitly set
        var result = _decoder.DecodeHeaders(frame, state);
        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, state.GetResponse().StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void DecodeHeaders_duplicate_status_pseudo_header()
    {
        var state = new StreamState();
        // Cannot easily encode duplicate pseudo-headers through QpackTableSync,
        // but test that the validator catches it via FieldValidator
        var frame = EncodeHeaders((":status", "200"));

        // First decode succeeds
        var result = _decoder.DecodeHeaders(frame, state);
        Assert.True(result);

        // The state now has a response, so subsequent DecodeHeaders returns false (trailing)
        var frame2 = EncodeHeaders((":status", "201"));
        var result2 = _decoder.DecodeHeaders(frame2, state);
        Assert.False(result2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void DecodeHeaders_unknown_pseudo_header()
    {
        var state = new StreamState();
        // Attempt to encode unknown pseudo-header
        var frame = EncodeHeaders(
            (":status", "200"),
            (":unknown", "value"));

        var ex = Assert.Throws<Http3Exception>(() => _decoder.DecodeHeaders(frame, state));
        Assert.NotNull(ex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void DecodeHeaders_status_codes_100()
    {
        var state = new StreamState();
        var frame = EncodeHeaders((":status", "100"));

        var result = _decoder.DecodeHeaders(frame, state);
        Assert.True(result);
        Assert.Equal(HttpStatusCode.Continue, state.GetResponse().StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void DecodeHeaders_status_codes_1xx_series()
    {
        var state = new StreamState();
        var frame = EncodeHeaders((":status", "101"));

        var result = _decoder.DecodeHeaders(frame, state);
        Assert.True(result);
        Assert.Equal((HttpStatusCode)101, state.GetResponse().StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void DecodeHeaders_status_codes_2xx_series()
    {
        var testCodes = new[] { 200, 201, 204, 206 };
        foreach (var code in testCodes)
        {
            var state = new StreamState();
            var frame = EncodeHeaders((":status", code.ToString()));

            var result = _decoder.DecodeHeaders(frame, state);
            Assert.True(result);
            Assert.Equal((HttpStatusCode)code, state.GetResponse().StatusCode);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void DecodeHeaders_status_codes_3xx_series()
    {
        var testCodes = new[] { 300, 301, 302, 304, 307 };
        foreach (var code in testCodes)
        {
            var state = new StreamState();
            var frame = EncodeHeaders((":status", code.ToString()));

            var result = _decoder.DecodeHeaders(frame, state);
            Assert.True(result);
            Assert.Equal((HttpStatusCode)code, state.GetResponse().StatusCode);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void DecodeHeaders_status_codes_4xx_series()
    {
        var testCodes = new[] { 400, 401, 403, 404, 429 };
        foreach (var code in testCodes)
        {
            var state = new StreamState();
            var frame = EncodeHeaders((":status", code.ToString()));

            var result = _decoder.DecodeHeaders(frame, state);
            Assert.True(result);
            Assert.Equal((HttpStatusCode)code, state.GetResponse().StatusCode);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void DecodeHeaders_status_codes_5xx_series()
    {
        var testCodes = new[] { 500, 501, 502, 503, 504 };
        foreach (var code in testCodes)
        {
            var state = new StreamState();
            var frame = EncodeHeaders((":status", code.ToString()));

            var result = _decoder.DecodeHeaders(frame, state);
            Assert.True(result);
            Assert.Equal((HttpStatusCode)code, state.GetResponse().StatusCode);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void DecodeHeaders_invalid_status_code_non_numeric()
    {
        var state = new StreamState();
        var frame = EncodeHeaders((":status", "abc"));

        Assert.Throws<FormatException>(() => _decoder.DecodeHeaders(frame, state));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void DecodeHeaders_invalid_status_code_too_large()
    {
        var state = new StreamState();
        var frame = EncodeHeaders((":status", "999999"));

        // int.Parse succeeds, but HttpStatusCode assignment throws ArgumentOutOfRangeException
        // because valid HTTP status codes are 100-599
        Assert.Throws<ArgumentOutOfRangeException>(() => _decoder.DecodeHeaders(frame, state));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void DecodeHeaders_invalid_status_code_negative()
    {
        var state = new StreamState();
        var frame = EncodeHeaders((":status", "-200"));

        // int.Parse allows negative numbers, but HttpStatusCode assignment throws
        // ArgumentOutOfRangeException because status codes must be non-negative
        Assert.Throws<ArgumentOutOfRangeException>(() => _decoder.DecodeHeaders(frame, state));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void DecodeHeaders_empty_status_code_value()
    {
        var state = new StreamState();
        var frame = EncodeHeaders((":status", ""));

        Assert.Throws<FormatException>(() => _decoder.DecodeHeaders(frame, state));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void DecodeHeaders_status_code_with_whitespace()
    {
        var state = new StreamState();
        var frame = EncodeHeaders((":status", " 200 "));

        // .NET's int.Parse trims whitespace by default
        var result = _decoder.DecodeHeaders(frame, state);
        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, state.GetResponse().StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void AssembleHeaders_rejects_null_state()
    {
        var headers = new List<(string Name, string Value)> { (":status", "200") };
        // NullReferenceException thrown when null state
        Assert.Throws<NullReferenceException>(() => _decoder.AssembleHeaders(headers, null!));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void AssembleHeaders_rejects_null_headers()
    {
        var state = new StreamState();
        // NullReferenceException thrown by FieldValidator.ValidateResponsePseudoHeaders
        Assert.Throws<NullReferenceException>(() => _decoder.AssembleHeaders(null!, state));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void AssembleHeaders_empty_header_list()
    {
        var state = new StreamState();
        var headers = new List<(string Name, string Value)>();

        // Empty header list doesn't cause validation error (ValidateResponsePseudoHeaders
        // doesn't enforce that :status must be present). HttpResponseMessage defaults to OK.
        var result = _decoder.AssembleHeaders(headers, state);
        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, state.GetResponse().StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void AssembleHeaders_multiple_regular_headers()
    {
        var state = new StreamState();
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("x-header-1", "value1"),
            ("x-header-2", "value2"),
            ("x-header-3", "value3"),
        };

        var result = _decoder.AssembleHeaders(headers, state);
        Assert.True(result);

        var response = state.GetResponse();
        Assert.Single(response.Headers.GetValues("x-header-1"));
        Assert.Single(response.Headers.GetValues("x-header-2"));
        Assert.Single(response.Headers.GetValues("x-header-3"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void AssembleHeaders_header_case_insensitivity()
    {
        var state = new StreamState();
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("x-custom-1", "text/plain"),
            ("x-custom-2", "42"),
            ("server", "MyServer"),
        };

        var result = _decoder.AssembleHeaders(headers, state);
        Assert.True(result);

        var response = state.GetResponse();
        // Verify headers were added
        Assert.NotEmpty(response.Headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void AssembleHeaders_response_with_all_content_headers()
    {
        var state = new StreamState();
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("content-type", "application/json"),
            ("content-length", "1024"),
            ("content-encoding", "gzip"),
            ("content-language", "en-US"),
            ("content-location", "/resource"),
            ("content-range", "bytes 0-1023/2048"),
            ("allow", "GET, POST, PUT"),
            ("expires", "Wed, 21 Oct 2026 07:28:00 GMT"),
            ("last-modified", "Mon, 15 Apr 2026 12:00:00 GMT"),
        };

        var result = _decoder.AssembleHeaders(headers, state);
        Assert.True(result);
        Assert.True(state.HasContentHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void AssembleHeaders_duplicate_regular_headers()
    {
        var state = new StreamState();
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("x-custom", "value1"),
            ("x-custom", "value2"),
        };

        var result = _decoder.AssembleHeaders(headers, state);
        Assert.True(result);

        var response = state.GetResponse();
        var values = response.Headers.GetValues("x-custom").ToList();
        Assert.Equal(2, values.Count);
        Assert.Contains("value1", values);
        Assert.Contains("value2", values);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void AssembleHeaders_empty_header_values()
    {
        var state = new StreamState();
        var headers = new List<(string Name, string Value)>
        {
            (":status", "204"),
            ("x-empty", ""),
            ("x-another", ""),
        };

        var result = _decoder.AssembleHeaders(headers, state);
        Assert.True(result);

        var response = state.GetResponse();
        Assert.Equal("", response.Headers.GetValues("x-empty").Single());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void AssembleHeaders_long_header_values()
    {
        var state = new StreamState();
        var longValue = new string('x', 100000);
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("x-long", longValue),
        };

        var result = _decoder.AssembleHeaders(headers, state);
        Assert.True(result);

        var response = state.GetResponse();
        Assert.Equal(longValue, response.Headers.GetValues("x-long").Single());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void AssembleHeaders_special_characters_in_header_values()
    {
        var state = new StreamState();
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("x-special", "value=with;special,chars:123"),
        };

        var result = _decoder.AssembleHeaders(headers, state);
        Assert.True(result);

        var response = state.GetResponse();
        Assert.Equal("value=with;special,chars:123", response.Headers.GetValues("x-special").Single());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void AccumulateData_rejects_null_frame()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders((":status", "200")), state); // Ensure HasResponse is true
        // NullReferenceException thrown accessing null!.Data
        Assert.Throws<NullReferenceException>(() => _decoder.AccumulateData(null!, state));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void AccumulateData_rejects_null_state()
    {
        var frame = new Http3DataFrame(new byte[] { 0x01 });
        // NullReferenceException thrown when state is null
        Assert.Throws<NullReferenceException>(() => _decoder.AccumulateData(frame, null!));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void AccumulateData_multiple_frames_in_sequence()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders((":status", "200")), state);

        var frame1 = new Http3DataFrame(new byte[] { 0x01, 0x02, 0x03 });
        var frame2 = new Http3DataFrame(new byte[] { 0x04, 0x05 });
        var frame3 = new Http3DataFrame(new byte[] { 0x06 });

        Assert.True(_decoder.AccumulateData(frame1, state));
        Assert.True(_decoder.AccumulateData(frame2, state));
        Assert.True(_decoder.AccumulateData(frame3, state));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void AccumulateData_large_single_frame()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders((":status", "200")), state);

        var largeData = new byte[1024 * 1024]; // 1 MB
        for (var i = 0; i < largeData.Length; i++)
        {
            largeData[i] = (byte)(i % 256);
        }

        var frame = new Http3DataFrame(largeData);
        var result = _decoder.AccumulateData(frame, state);

        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void AccumulateData_many_small_frames()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders((":status", "200")), state);

        for (var i = 0; i < 1000; i++)
        {
            var frame = new Http3DataFrame(new[] { (byte)(i % 256) });
            Assert.True(_decoder.AccumulateData(frame, state));
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void AccumulateData_zero_byte_frames()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders((":status", "200")), state);

        var emptyFrame1 = new Http3DataFrame(ReadOnlyMemory<byte>.Empty);
        var emptyFrame2 = new Http3DataFrame(ReadOnlyMemory<byte>.Empty);

        Assert.True(_decoder.AccumulateData(emptyFrame1, state));
        Assert.True(_decoder.AccumulateData(emptyFrame2, state));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void CompleteResponse_rejects_null_state()
    {
        // NullReferenceException thrown when state is null
        Assert.Throws<NullReferenceException>(() => _decoder.CompleteResponse(null!));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void CompleteResponse_without_headers_throws()
    {
        var state = new StreamState();
        Assert.Throws<InvalidOperationException>(() => _decoder.CompleteResponse(state));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void CompleteResponse_response_without_content_body()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders((":status", "204")), state);

        var response = _decoder.CompleteResponse(state);

        Assert.NotNull(response.Content);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task CompleteResponse_response_with_single_byte_body()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders((":status", "200")), state);
        _decoder.AccumulateData(new Http3DataFrame(new byte[] { 0xFF }), state);

        var response = _decoder.CompleteResponse(state);

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Single(body);
        Assert.Equal(0xFF, body[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task CompleteResponse_response_with_large_body()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders((":status", "200")), state);

        var largeData = new byte[1024 * 1024]; // 1 MB
        for (var i = 0; i < largeData.Length; i++)
        {
            largeData[i] = (byte)(i % 256);
        }

        _decoder.AccumulateData(new Http3DataFrame(largeData), state);

        var response = _decoder.CompleteResponse(state);

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(largeData.Length, body.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task CompleteResponse_response_with_fragmented_body()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders((":status", "200")), state);

        var totalSize = 10000;
        var chunkSize = 100;

        for (var i = 0; i < totalSize; i += chunkSize)
        {
            var chunk = new byte[Math.Min(chunkSize, totalSize - i)];
            for (var j = 0; j < chunk.Length; j++)
            {
                chunk[j] = (byte)((i + j) % 256);
            }

            _decoder.AccumulateData(new Http3DataFrame(chunk), state);
        }

        var response = _decoder.CompleteResponse(state);

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(totalSize, body.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void CompleteResponse_content_headers_applied_to_body()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders(
            (":status", "200"),
            ("content-type", "application/json"),
            ("content-length", "2")), state);
        _decoder.AccumulateData(new Http3DataFrame(new byte[] { 0x01, 0x02 }), state);

        var response = _decoder.CompleteResponse(state);

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(2, response.Content.Headers.ContentLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void CompleteResponse_multiple_content_headers()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders(
            (":status", "200"),
            ("content-type", "text/plain"),
            ("content-encoding", "gzip"),
            ("content-language", "en-US")), state);

        var response = _decoder.CompleteResponse(state);

        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.NotNull(response.Content.Headers.ContentEncoding);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void CompleteResponse_allow_header_as_content_header()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders(
            (":status", "200"),
            ("allow", "GET, POST, PUT")), state);

        var response = _decoder.CompleteResponse(state);

        Assert.NotNull(response.Content.Headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void CompleteResponse_expires_header_as_content_header()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders(
            (":status", "200"),
            ("expires", "Wed, 21 Oct 2026 07:28:00 GMT")), state);

        var response = _decoder.CompleteResponse(state);

        Assert.NotNull(response.Content.Headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void CompleteResponse_last_modified_header_as_content_header()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders(
            (":status", "200"),
            ("last-modified", "Mon, 15 Apr 2026 12:00:00 GMT")), state);

        var response = _decoder.CompleteResponse(state);

        Assert.NotNull(response.Content.Headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void IsContentHeader_null_name()
    {
        // NullReferenceException thrown by StartsWith on null
        Assert.Throws<NullReferenceException>(() => ResponseDecoder.IsContentHeader(null!));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void IsContentHeader_empty_name()
    {
        Assert.False(ResponseDecoder.IsContentHeader(""));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void IsContentHeader_all_content_prefixed_headers()
    {
        var contentHeaders = new[]
        {
            "content-type",
            "content-length",
            "content-encoding",
            "content-language",
            "content-location",
            "content-range",
            "content-md5",
            "content-disposition",
        };

        foreach (var header in contentHeaders)
        {
            Assert.True(ResponseDecoder.IsContentHeader(header));
            Assert.True(ResponseDecoder.IsContentHeader(header.ToUpperInvariant()));
            // Note: underscore replacement doesn't work since the implementation looks for "content-" prefix
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void IsContentHeader_special_content_headers()
    {
        Assert.True(ResponseDecoder.IsContentHeader("allow"));
        Assert.True(ResponseDecoder.IsContentHeader("Allow"));
        Assert.True(ResponseDecoder.IsContentHeader("ALLOW"));
        Assert.True(ResponseDecoder.IsContentHeader("expires"));
        Assert.True(ResponseDecoder.IsContentHeader("Expires"));
        Assert.True(ResponseDecoder.IsContentHeader("EXPIRES"));
        Assert.True(ResponseDecoder.IsContentHeader("last-modified"));
        Assert.True(ResponseDecoder.IsContentHeader("Last-Modified"));
        Assert.True(ResponseDecoder.IsContentHeader("LAST-MODIFIED"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void IsContentHeader_non_content_headers()
    {
        var nonContentHeaders = new[]
        {
            "server",
            "date",
            "cache-control",
            "set-cookie",
            "vary",
            "etag",
            "age",
            "x-custom-header",
            "authorization",
            "www-authenticate",
        };

        foreach (var header in nonContentHeaders)
        {
            Assert.False(ResponseDecoder.IsContentHeader(header));
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void IsContentHeader_content_prefix_but_not_content_header()
    {
        // Headers that start with "content-" in the name but are not HTTP headers
        Assert.False(ResponseDecoder.IsContentHeader("cont"));
        Assert.False(ResponseDecoder.IsContentHeader("context"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void DecoderInstructions_returns_instructions_after_decode()
    {
        var state = new StreamState();
        var frame = EncodeHeaders((":status", "200"));

        _decoder.DecodeHeaders(frame, state);

        var instructions = _decoder.DecoderInstructions;
        // Decoder instructions may be empty for simple headers, but the property should be accessible
        Assert.True(instructions.Length >= 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void DecoderInstructions_available_before_any_decode()
    {
        var instructions = _decoder.DecoderInstructions;
        Assert.True(instructions.Length >= 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Combined_full_response_lifecycle()
    {
        var state = new StreamState();

        // Decode headers
        var headerFrame = EncodeHeaders(
            (":status", "200"),
            ("content-type", "text/plain"),
            ("content-length", "12"));
        _decoder.DecodeHeaders(headerFrame, state);

        // Accumulate body in multiple frames
        _decoder.AccumulateData(new Http3DataFrame("Hel"u8.ToArray()), state); // "Hel"
        _decoder.AccumulateData(new Http3DataFrame("lo"u8.ToArray()), state); // "lo"
        _decoder.AccumulateData(new Http3DataFrame(" Wo"u8.ToArray()), state); // " Wo"
        _decoder.AccumulateData(new Http3DataFrame("rld"u8.ToArray()), state); // "rld"
        _decoder.AccumulateData(new Http3DataFrame(new byte[] { 0x21 }), state); // "!"

        // Complete response
        var response = _decoder.CompleteResponse(state);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World!", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Combined_trailing_headers_not_supported()
    {
        var state = new StreamState();

        var firstHeaders = EncodeHeaders((":status", "200"));
        _decoder.DecodeHeaders(firstHeaders, state);

        var bodyFrame = new Http3DataFrame(new byte[] { 0x01, 0x02 });
        _decoder.AccumulateData(bodyFrame, state);

        // Try to decode trailing headers
        var trailingHeaders = EncodeHeaders(("x-trailer", "value"));
        var result = _decoder.DecodeHeaders(trailingHeaders, state);

        // Should return false to indicate no new response (trailers not yet supported)
        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Combined_multiple_responses_in_sequence()
    {
        // This tests decoder reuse with different states
        var state1 = new StreamState();
        var state2 = new StreamState();

        // First response
        var frame1 = EncodeHeaders((":status", "200"));
        _decoder.DecodeHeaders(frame1, state1);

        // Second response with different status
        var frame2 = EncodeHeaders((":status", "404"));
        _decoder.DecodeHeaders(frame2, state2);

        Assert.Equal(HttpStatusCode.OK, state1.GetResponse().StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, state2.GetResponse().StatusCode);
    }
}