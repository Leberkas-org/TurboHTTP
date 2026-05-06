using TurboHTTP.Protocol.Http3;
using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Connection;

public sealed class Http3ContentLengthSpec
{
    private readonly QpackTableSync _tableSync = new(encoderMaxCapacity: 0);
    private readonly ResponseDecoder _decoder;

    public Http3ContentLengthSpec()
    {
        _decoder = new ResponseDecoder(_tableSync);
    }

    private HeadersFrame EncodeHeaders(params (string Name, string Value)[] headers)
    {
        return new HeadersFrame(_tableSync.Encoder.Encode(headers));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1.2")]
    public void CompleteResponse_should_succeed_when_content_length_matches()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders(
            (":status", "200"),
            ("content-length", "5")), state);

        _decoder.AccumulateData(new DataFrame("Hello"u8.ToArray()), state);

        var response = _decoder.CompleteResponse(state);
        Assert.NotNull(response);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1.2")]
    public void CompleteResponse_should_throw_when_body_too_short()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders(
            (":status", "200"),
            ("content-length", "10")), state);

        _decoder.AccumulateData(new DataFrame("Short"u8.ToArray()), state);

        var ex = Assert.Throws<Http3Exception>(() => _decoder.CompleteResponse(state));
        Assert.Contains("Content-Length mismatch", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1.2")]
    public void CompleteResponse_should_throw_when_body_too_long()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders(
            (":status", "200"),
            ("content-length", "3")), state);

        _decoder.AccumulateData(new DataFrame("TooLong"u8.ToArray()), state);

        var ex = Assert.Throws<Http3Exception>(() => _decoder.CompleteResponse(state));
        Assert.Contains("Content-Length mismatch", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1.2")]
    public void CompleteResponse_should_skip_validation_without_content_length()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders(
            (":status", "200"),
            ("content-type", "text/plain")), state);

        _decoder.AccumulateData(new DataFrame("Any length"u8.ToArray()), state);

        var response = _decoder.CompleteResponse(state);
        Assert.NotNull(response);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1.2")]
    public void CompleteResponse_should_succeed_with_zero_content_length_and_no_body()
    {
        var state = new StreamState();
        _decoder.DecodeHeaders(EncodeHeaders(
            (":status", "204"),
            ("content-length", "0")), state);

        var response = _decoder.CompleteResponse(state);
        Assert.NotNull(response);
    }
}
