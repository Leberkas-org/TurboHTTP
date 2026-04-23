using Servus.Akka.IO;
using TurboHTTP.Streams;

namespace TurboHTTP.Tests.Semantics;

public sealed class ProtocolCoreBuilderLimitsSpec
{
    private static RequestEndpoint EndpointForVersion(int major, int minor) => new()
    {
        Scheme = "https",
        Host = "example.com",
        Port = 443,
        Version = new Version(major, minor)
    };

    [Fact(Timeout = 5000)]
    public void MaxSubstreamsPerKey_should_use_h2_limit_for_http2_endpoint()
    {
        var endpoint = EndpointForVersion(2, 0);

        var result = ProtocolCoreBuilder.GetMaxSubstreamsPerKey(endpoint, maxConnsH1: 10, maxConnsH2: 6, maxConnsH3: 4);

        Assert.Equal(6, result);
    }

    [Fact(Timeout = 5000)]
    public void MaxSubstreamsPerKey_should_use_h3_limit_for_http3_endpoint()
    {
        var endpoint = EndpointForVersion(3, 0);

        var result = ProtocolCoreBuilder.GetMaxSubstreamsPerKey(endpoint, maxConnsH1: 10, maxConnsH2: 6, maxConnsH3: 4);

        Assert.Equal(4, result);
    }

    [Fact(Timeout = 5000)]
    public void MaxSubstreamsPerKey_should_use_h1_limit_for_http11_endpoint()
    {
        var endpoint = EndpointForVersion(1, 1);

        var result = ProtocolCoreBuilder.GetMaxSubstreamsPerKey(endpoint, maxConnsH1: 10, maxConnsH2: 6, maxConnsH3: 4);

        Assert.Equal(10, result);
    }

    [Fact(Timeout = 5000)]
    public void MaxConcurrencyPerSlot_should_use_h2_streams_for_http2_endpoint()
    {
        var endpoint = EndpointForVersion(2, 0);

        var result = ProtocolCoreBuilder.GetMaxConcurrencyPerSlot(endpoint, h2Streams: 100, h1Streams: 8);

        Assert.Equal(100, result);
    }

    [Fact(Timeout = 5000)]
    public void MaxConcurrencyPerSlot_should_use_int_max_for_http3_endpoint()
    {
        var endpoint = EndpointForVersion(3, 0);

        var result = ProtocolCoreBuilder.GetMaxConcurrencyPerSlot(endpoint, h2Streams: 100, h1Streams: 8);

        Assert.Equal(int.MaxValue, result);
    }

    [Fact(Timeout = 5000)]
    public void MaxConcurrencyPerSlot_should_use_pipeline_depth_for_http11_endpoint()
    {
        var endpoint = EndpointForVersion(1, 1);

        var result =
            ProtocolCoreBuilder.GetMaxConcurrencyPerSlot(endpoint, h2Streams: 100, h1Streams: 8);

        Assert.Equal(8, result);
    }

    [Fact(Timeout = 5000)]
    public void MaxConcurrencyPerSlot_should_return_one_for_http10_endpoint()
    {
        var endpoint = EndpointForVersion(1, 0);

        var result =
            ProtocolCoreBuilder.GetMaxConcurrencyPerSlot(endpoint, h2Streams: 100, h1Streams: 8);

        Assert.Equal(1, result);
    }

    [Fact(Timeout = 5000)]
    public void Limits_should_be_independent_between_h2_and_h3()
    {
        var h2Endpoint = EndpointForVersion(2, 0);
        var h3Endpoint = EndpointForVersion(3, 0);

        var h2Substreams =
            ProtocolCoreBuilder.GetMaxSubstreamsPerKey(h2Endpoint, maxConnsH1: 10, maxConnsH2: 6, maxConnsH3: 4);
        var h3Substreams =
            ProtocolCoreBuilder.GetMaxSubstreamsPerKey(h3Endpoint, maxConnsH1: 10, maxConnsH2: 6, maxConnsH3: 4);
        var h2Concurrency =
            ProtocolCoreBuilder.GetMaxConcurrencyPerSlot(h2Endpoint, h2Streams: 100, h1Streams: 8);
        var h3Concurrency =
            ProtocolCoreBuilder.GetMaxConcurrencyPerSlot(h3Endpoint, h2Streams: 100, h1Streams: 8);

        Assert.NotEqual(h2Substreams, h3Substreams);
        Assert.NotEqual(h2Concurrency, h3Concurrency);
        Assert.Equal(6, h2Substreams);
        Assert.Equal(4, h3Substreams);
        Assert.Equal(100, h2Concurrency);
        Assert.Equal(int.MaxValue, h3Concurrency);
    }
}