using System.Net;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Connection;

public sealed class Http3ResponseDecoderSpec
{
    private readonly QpackTableSync _tableSync = new();
    private readonly ResponseDecoder _decoder;

    public Http3ResponseDecoderSpec()
    {
        _decoder = new ResponseDecoder(_tableSync);
    }

    private Http3HeadersFrame EncodeHeaders(params (string Name, string Value)[] headers)
    {
        return new Http3HeadersFrame(_tableSync.Encoder.Encode(headers));
    }

    [Fact(Timeout = 5000)]
    public void DecodeHeaders_should_parse_status_code()
    {
        var state = new StreamState();
        var frame = EncodeHeaders((":status", "200"));

        var result = _decoder.DecodeHeaders(frame, state);

        Assert.True(result);
        Assert.True(state.HasResponse);
        Assert.Equal(HttpStatusCode.OK, state.GetResponse().StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void DecodeHeaders_should_parse_response_headers()
    {
        var state = new StreamState();
        var frame = EncodeHeaders(
            (":status", "200"),
            ("x-custom", "value"),
            ("server", "test"));

        _decoder.DecodeHeaders(frame, state);

        var response = state.GetResponse();
        Assert.Equal("value", response.Headers.GetValues("x-custom").Single());
        Assert.Equal("test", response.Headers.GetValues("server").Single());
    }

    [Fact(Timeout = 5000)]
    public void DecodeHeaders_should_capture_content_headers()
    {
        var state = new StreamState();
        var frame = EncodeHeaders(
            (":status", "200"),
            ("content-type", "text/plain"),
            ("content-length", "42"));

        _decoder.DecodeHeaders(frame, state);

        Assert.True(state.HasContentHeaders);
    }

    [Fact(Timeout = 5000)]
    public void DecodeHeaders_should_skip_trailing_headers()
    {
        var state = new StreamState();
        var first = EncodeHeaders((":status", "200"));
        var trailing = EncodeHeaders(("x-trailer", "value"));

        _decoder.DecodeHeaders(first, state);
        var result = _decoder.DecodeHeaders(trailing, state);

        Assert.False(result);
        Assert.Equal(HttpStatusCode.OK, state.GetResponse().StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void AccumulateData_should_reject_data_before_headers()
    {
        var state = new StreamState();
        var frame = new Http3DataFrame(new byte[] { 0x01, 0x02 });

        var result = _decoder.AccumulateData(frame, state);

        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    public void AccumulateData_should_append_body_after_headers()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders((":status", "200")), state);

        var result = _decoder.AccumulateData(new Http3DataFrame("AB"u8.ToArray()), state);

        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public void AccumulateData_should_handle_empty_data_frame()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders((":status", "200")), state);

        var result = _decoder.AccumulateData(new Http3DataFrame(ReadOnlyMemory<byte>.Empty), state);

        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public async Task CompleteResponse_should_assemble_body_with_content_headers()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders(
            (":status", "200"),
            ("content-type", "application/json")), state);
        _decoder.AccumulateData(new Http3DataFrame("{}"u8.ToArray()), state);

        var response = _decoder.CompleteResponse(state);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Content);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("{}"u8.ToArray(), body);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact(Timeout = 5000)]
    public void CompleteResponse_should_assemble_headers_only_response()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders((":status", "204")), state);

        var response = _decoder.CompleteResponse(state);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotNull(response.Content);
    }

    [Fact(Timeout = 5000)]
    public void CompleteResponse_should_apply_content_headers_to_empty_body()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders(
            (":status", "200"),
            ("content-length", "0")), state);

        var response = _decoder.CompleteResponse(state);

        Assert.Equal("0", response.Content.Headers.ContentLength?.ToString());
    }

    [Fact(Timeout = 5000)]
    public async Task CompleteResponse_should_accumulate_multiple_data_frames()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders((":status", "200")), state);
        _decoder.AccumulateData(new Http3DataFrame(new byte[] { 0x41 }), state);
        _decoder.AccumulateData(new Http3DataFrame("BC"u8.ToArray()), state);

        var response = _decoder.CompleteResponse(state);

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("ABC"u8.ToArray(), body);
    }

    [Fact(Timeout = 5000)]
    public void IsContentHeader_should_identify_content_headers()
    {
        Assert.True(ResponseDecoder.IsContentHeader("content-type"));
        Assert.True(ResponseDecoder.IsContentHeader("content-length"));
        Assert.True(ResponseDecoder.IsContentHeader("Content-Type"));
        Assert.True(ResponseDecoder.IsContentHeader("allow"));
        Assert.True(ResponseDecoder.IsContentHeader("expires"));
        Assert.True(ResponseDecoder.IsContentHeader("last-modified"));
    }

    [Fact(Timeout = 5000)]
    public void IsContentHeader_should_reject_non_content_headers()
    {
        Assert.False(ResponseDecoder.IsContentHeader("server"));
        Assert.False(ResponseDecoder.IsContentHeader("x-custom"));
        Assert.False(ResponseDecoder.IsContentHeader("cache-control"));
    }
}