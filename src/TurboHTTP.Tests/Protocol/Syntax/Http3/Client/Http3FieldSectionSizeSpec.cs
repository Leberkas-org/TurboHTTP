using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Client;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client;

public sealed class Http3FieldSectionSizeSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2.2")]
    public void ResponseDecoder_should_reject_oversized_field_section()
    {
        var tableSync = new QpackTableSync(encoderMaxCapacity: 0);
        var decoder = new Http3ClientDecoder(tableSync, maxFieldSectionSize: 64);

        var longValue = new string('x', 100);
        var headerFrame = new HeadersFrame(
            tableSync.Encoder.Encode([(":status", "200"), ("x-big", longValue)]));

        var state = new StreamState();

        var ex = Assert.Throws<HttpProtocolException>(() => decoder.DecodeHeaders(headerFrame, state));
        Assert.Contains("SETTINGS_MAX_FIELD_SECTION_SIZE", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2.2")]
    public void ResponseDecoder_should_accept_field_section_within_limit()
    {
        var tableSync = new QpackTableSync(encoderMaxCapacity: 0);
        var decoder = new Http3ClientDecoder(tableSync, maxFieldSectionSize: 65536);

        var headerFrame = new HeadersFrame(
            tableSync.Encoder.Encode([(":status", "200"), ("x-small", "ok")]));

        var state = new StreamState();
        var result = decoder.DecodeHeaders(headerFrame, state);

        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2.2")]
    public void RequestEncoder_should_reject_headers_exceeding_peer_limit()
    {
        var tableSync = new QpackTableSync(encoderMaxCapacity: 0)
        {
            RemoteMaxFieldSectionSize = 32
        };

        var encoder = new Http3ClientEncoder(tableSync);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/test");
        request.Headers.TryAddWithoutValidation("x-big", new string('x', 100));

        Assert.Throws<HttpProtocolException>(() => encoder.Encode(request));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2.2")]
    public void RequestEncoder_should_allow_headers_within_peer_limit()
    {
        var tableSync = new QpackTableSync(encoderMaxCapacity: 0)
        {
            RemoteMaxFieldSectionSize = 65536
        };

        var encoder = new Http3ClientEncoder(tableSync);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        var frames = encoder.Encode(request);

        Assert.NotEmpty(frames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2.2")]
    public void RequestEncoder_should_skip_check_when_no_peer_limit()
    {
        var tableSync = new QpackTableSync(encoderMaxCapacity: 0);

        var encoder = new Http3ClientEncoder(tableSync);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("x-big", new string('x', 1000));

        var frames = encoder.Encode(request);

        Assert.NotEmpty(frames);
    }
}