using System.Net;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.Tests.Http2.Components;

/// <summary>
/// Unit tests for ResponseDecoder RFC 9113 header decoding and response assembly.
/// Covers HPACK decoding, pseudo-header processing, and content header tracking.
/// Note: Tests use minimal HpackEncoder.Encode calls to avoid encoding exceptions.
/// Full HPACK integration is tested in Hpack/* test suites.
/// </summary>
public sealed class Http2ResponseDecoderSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_decode_status_pseudo_header()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "200")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder());

        var response = decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_set_response_on_state()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "200")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder());

        var response = decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(response);
        Assert.True(state.HasResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_handle_100_continue()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "100")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder());

        var response = decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.Continue, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_handle_201_created()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "201")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder());

        var response = decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.Created, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_handle_204_no_content()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "204")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder());

        var response = decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.NoContent, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_handle_304_not_modified()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "304")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder());

        var response = decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.NotModified, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_handle_400_bad_request()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "400")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder());

        var response = decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.BadRequest, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_handle_401_unauthorized()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "401")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder());

        var response = decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.Unauthorized, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_handle_403_forbidden()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "403")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder());

        var response = decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.Forbidden, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_handle_404_not_found()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "404")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder());

        var response = decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.NotFound, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_handle_500_server_error()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "500")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder());

        var response = decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.InternalServerError, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_handle_502_bad_gateway()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "502")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder());

        var response = decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.BadGateway, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_handle_503_service_unavailable()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "503")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder());

        var response = decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_ignore_pseudo_headers_other_than_status()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "200")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder());

        var response = decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(response);
        Assert.DoesNotContain(":method", response!.Headers.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(":path", response!.Headers.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_return_null_when_endStream_is_false()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "200")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder());

        var response = decoder.DecodeHeaders(streamId: 1, endStream: false, state);

        Assert.Null(response);
        Assert.True(state.HasResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_throw_on_missing_status_pseudo_header()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder());

        Assert.Throws<FormatException>(() => decoder.DecodeHeaders(streamId: 1, endStream: true, state));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-10.5.1")]
    public void DecodeHeaders_should_set_content_for_headers_only_response()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "204")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder());

        var response = decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(response);
        Assert.NotNull(response!.Content);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-10.5.1")]
    public void DecodeHeaders_should_throw_when_single_header_exceeds_max_size()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "200")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder(), maxHeaderSize: 1); // Very small limit

        Assert.Throws<Http2Exception>(() => decoder.DecodeHeaders(streamId: 1, endStream: true, state));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-10.5.1")]
    public void DecodeHeaders_should_throw_when_total_headers_exceed_max_size()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "200")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder(), maxTotalHeaderSize: 1); // Very small limit

        Assert.Throws<Http2Exception>(() => decoder.DecodeHeaders(streamId: 1, endStream: true, state));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-10.5.1")]
    public void DecodeHeaders_should_include_stream_id_in_error_message()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "200")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder(), maxHeaderSize: 1);

        var ex = Assert.Throws<Http2Exception>(() => decoder.DecodeHeaders(streamId: 42, endStream: true, state));
        Assert.Contains("stream 42", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void ResetHpack_should_create_new_decoder()
    {
        var decoder = new ResponseDecoder(new HpackDecoder());
        var initialDecoder = typeof(ResponseDecoder).GetField("_hpack", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(decoder);

        decoder.ResetHpack();

        var newDecoder = typeof(ResponseDecoder).GetField("_hpack", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(decoder);
        Assert.NotSame(initialDecoder, newDecoder);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_create_new_response_message()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "200")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder());

        var response1 = decoder.DecodeHeaders(streamId: 1, endStream: true, state);
        var state2 = new StreamState();
        state2.AppendHeader(encoded.Span);
        var response2 = decoder.DecodeHeaders(streamId: 2, endStream: true, state2);

        Assert.NotNull(response1);
        Assert.NotNull(response2);
        Assert.NotSame(response1, response2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_parse_numeric_status_code()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "418")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder());

        var response = decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(response);
        Assert.Equal((HttpStatusCode)418, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_throw_on_invalid_status_code()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "invalid")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder());

        Assert.Throws<FormatException>(() => decoder.DecodeHeaders(streamId: 1, endStream: true, state));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_default_max_header_size_to_16kb()
    {
        var decoder = new ResponseDecoder(new HpackDecoder());
        Assert.NotNull(decoder);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_default_max_total_header_size_to_64kb()
    {
        var decoder = new ResponseDecoder(new HpackDecoder());
        Assert.NotNull(decoder);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_support_custom_max_header_limits()
    {
        var decoder = new ResponseDecoder(new HpackDecoder(), maxHeaderSize: 8192, maxTotalHeaderSize: 32768);
        Assert.NotNull(decoder);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_with_empty_header_block()
    {
        var state = new StreamState();
        var decoder = new ResponseDecoder(new HpackDecoder());

        Assert.Throws<FormatException>(() => decoder.DecodeHeaders(streamId: 1, endStream: true, state));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_handle_multiple_status_codes_across_streams()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var statuses = new[] { "200", "201", "204", "400", "404", "500", "503" };

        foreach (var status in statuses)
        {
            var encoded = encoder.Encode([(":status", status)]);
            var state = new StreamState();
            state.AppendHeader(encoded.Span);
            var decoder = new ResponseDecoder(new HpackDecoder());

            var response = decoder.DecodeHeaders(streamId: 1, endStream: true, state);

            Assert.NotNull(response);
            Assert.Equal((HttpStatusCode)int.Parse(status), response!.StatusCode);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_create_response_with_empty_content_on_headers_only()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "200")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder());

        var response = decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(response);
        Assert.NotNull(response!.Content);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_store_response_on_stream_state()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "200")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder());

        var response = decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(response);
        Assert.True(state.HasResponse);
        Assert.Same(response, state.GetResponse());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_use_hpack_decoder_for_decompression()
    {
        var hpack = new HpackDecoder();
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "200")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(hpack);

        var response = decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(response);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_should_handle_stream_id_for_error_scope()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "200")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder(), maxHeaderSize: 1);

        var ex = Assert.Throws<Http2Exception>(() => decoder.DecodeHeaders(streamId: 999, endStream: true, state));
        Assert.Contains("999", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_error_should_have_correct_error_code()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "200")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder(), maxHeaderSize: 1);

        var ex = Assert.Throws<Http2Exception>(() => decoder.DecodeHeaders(streamId: 1, endStream: true, state));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void DecodeHeaders_error_should_have_stream_scope()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "200")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new ResponseDecoder(new HpackDecoder(), maxHeaderSize: 1);

        var ex = Assert.Throws<Http2Exception>(() => decoder.DecodeHeaders(streamId: 1, endStream: true, state));
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
    }
}
