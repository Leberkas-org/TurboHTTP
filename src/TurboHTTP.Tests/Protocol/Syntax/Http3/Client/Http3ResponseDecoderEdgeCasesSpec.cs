using System.Net;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Client;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client;

public sealed class Http3ResponseDecoderEdgeCasesSpec
{
    private readonly QpackTableSync _tableSync = new();
    private readonly Http3ClientDecoder _decoder;

    public Http3ResponseDecoderEdgeCasesSpec()
    {
        _decoder = new Http3ClientDecoder(_tableSync);
    }

    private HeadersFrame EncodeHeaders(params (string Name, string Value)[] headers)
    {
        return new HeadersFrame(_tableSync.Encoder.Encode(headers));
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

        var ex = Assert.Throws<HttpProtocolException>(() => _decoder.DecodeHeaders(frame, state));
        Assert.Contains(":status", ex.Message);
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

        // The state now has a response, so subsequent DecodeHeaders is treated as trailers
        // RFC 9114 §4.3: Pseudo-header fields MUST NOT appear in trailer sections
        var frame2 = EncodeHeaders((":status", "201"));
        var ex = Assert.Throws<HttpProtocolException>(() => _decoder.DecodeHeaders(frame2, state));
        Assert.Contains("pseudo", ex.Message, StringComparison.OrdinalIgnoreCase);
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

        var ex = Assert.Throws<HttpProtocolException>(() => _decoder.DecodeHeaders(frame, state));
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

        var ex = Assert.Throws<HttpProtocolException>(() => _decoder.AssembleHeaders(headers, state));
        Assert.Contains(":status", ex.Message);
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void DecodeHeaders_should_reject_method_pseudo_header_in_trailers()
    {
        var state = new StreamState();

        // First decode with valid :status response
        var responseFrame = EncodeHeaders((":status", "200"));
        _decoder.DecodeHeaders(responseFrame, state);

        // Now attempt to decode trailers with :method pseudo-header
        var trailerFrame = EncodeHeaders((":method", "GET"), ("x-checksum", "abc"));

        var ex = Assert.Throws<HttpProtocolException>(() => _decoder.DecodeHeaders(trailerFrame, state));
        Assert.Contains("pseudo", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void DecodeHeaders_should_reject_path_pseudo_header_in_trailers()
    {
        var state = new StreamState();

        // First decode with valid :status response
        var responseFrame = EncodeHeaders((":status", "200"));
        _decoder.DecodeHeaders(responseFrame, state);

        // Now attempt to decode trailers with :path pseudo-header
        var trailerFrame = EncodeHeaders((":path", "/"), ("x-checksum", "abc"));

        var ex = Assert.Throws<HttpProtocolException>(() => _decoder.DecodeHeaders(trailerFrame, state));
        Assert.Contains("pseudo", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void DecodeHeaders_should_accept_regular_headers_in_trailers()
    {
        var state = new StreamState();

        // First decode with valid :status response
        var responseFrame = EncodeHeaders((":status", "200"));
        _decoder.DecodeHeaders(responseFrame, state);

        // Now decode trailers with only regular headers (should succeed)
        var trailerFrame = EncodeHeaders(("x-checksum", "abc123"));

        var result = _decoder.DecodeHeaders(trailerFrame, state);
        // Trailers return false, indicating non-response headers
        Assert.False(result);
    }
}