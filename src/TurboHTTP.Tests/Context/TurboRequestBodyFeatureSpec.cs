using Akka.Streams.Dsl;
using TurboHTTP.Context.Features;

namespace TurboHTTP.Tests.Context;

public sealed class TurboRequestBodyFeatureSpec
{
    [Fact(Timeout = 5000)]
    public void TurboRequestBodyFeature_should_have_default_body_stream_null()
    {
        var feature = new TurboRequestBodyFeature();
        Assert.Equal(Stream.Null, feature.Body);
    }

    [Fact(Timeout = 5000)]
    public void TurboRequestBodyFeature_should_have_default_empty_body_source()
    {
        var feature = new TurboRequestBodyFeature();
        Assert.NotNull(feature.BodySource);
    }

    [Fact(Timeout = 5000)]
    public void TurboRequestBodyFeature_should_allow_setting_body()
    {
        var stream = new MemoryStream([1, 2, 3]);
        var feature = new TurboRequestBodyFeature { Body = stream };
        Assert.Same(stream, feature.Body);
    }

    [Fact(Timeout = 5000)]
    public void TurboRequestBodyFeature_should_allow_setting_body_source()
    {
        var source = Source.Single<ReadOnlyMemory<byte>>(new byte[] { 1, 2, 3 });
        var feature = new TurboRequestBodyFeature { BodySource = source };
        Assert.Same(source, feature.BodySource);
    }
}