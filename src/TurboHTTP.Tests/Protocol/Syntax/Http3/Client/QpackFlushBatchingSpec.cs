using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Client;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client;

public sealed class QpackFlushBatchingSpec
{
    private static QpackStreamManager CreateManager(FakeOps ops)
    {
        var tableSync = new QpackTableSync(
            encoderMaxCapacity: 4 * 1024,
            decoderMaxCapacity: 4 * 1024,
            maxBlockedStreams: 100,
            configuredEncoderLimit: 4 * 1024);

        var encoder = new Http3ClientEncoder(tableSync);
        var decoder = new Http3ClientDecoder(tableSync, 16 * 1024);

        return new QpackStreamManager(ops, encoder, decoder, tableSync);
    }

    [Fact(Timeout = 5000)]
    public void AccumulateEncoderInstructions_should_not_emit_immediately()
    {
        var ops = new FakeOps();
        var tableSync = new QpackTableSync(
            encoderMaxCapacity: 4 * 1024,
            decoderMaxCapacity: 4 * 1024,
            maxBlockedStreams: 100,
            configuredEncoderLimit: 4 * 1024);
        var encoder = new Http3ClientEncoder(tableSync);
        var decoder = new Http3ClientDecoder(tableSync, 16 * 1024);
        var mgr = new QpackStreamManager(ops, encoder, decoder, tableSync);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("x-custom-header", "custom-value");
        encoder.Encode(request);

        mgr.AccumulateEncoderInstructions();

        var outboundCount = ops.Outbound.Count(o => o is MultiplexedData);
        Assert.Equal(0, outboundCount);
    }

    [Fact(Timeout = 5000)]
    public void FlushIfNeeded_with_force_should_emit_accumulated_instructions()
    {
        var ops = new FakeOps();
        var tableSync = new QpackTableSync(
            encoderMaxCapacity: 4 * 1024,
            decoderMaxCapacity: 4 * 1024,
            maxBlockedStreams: 100,
            configuredEncoderLimit: 4 * 1024);
        var encoder = new Http3ClientEncoder(tableSync);
        var decoder = new Http3ClientDecoder(tableSync, 16 * 1024);
        var mgr = new QpackStreamManager(ops, encoder, decoder, tableSync);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("x-custom-header", "custom-value");
        encoder.Encode(request);

        mgr.AccumulateEncoderInstructions();
        mgr.FlushIfNeeded(force: true);

        var dataItems = ops.Outbound.OfType<MultiplexedData>().ToList();
        Assert.NotEmpty(dataItems);
    }

    [Fact(Timeout = 5000)]
    public void FlushPendingInstructions_should_flush_accumulated()
    {
        var ops = new FakeOps();
        var tableSync = new QpackTableSync(
            encoderMaxCapacity: 4 * 1024,
            decoderMaxCapacity: 4 * 1024,
            maxBlockedStreams: 100,
            configuredEncoderLimit: 4 * 1024);
        var encoder = new Http3ClientEncoder(tableSync);
        var decoder = new Http3ClientDecoder(tableSync, 16 * 1024);
        var mgr = new QpackStreamManager(ops, encoder, decoder, tableSync);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("x-custom-header", "custom-value");
        encoder.Encode(request);

        mgr.AccumulateEncoderInstructions();
        mgr.FlushPendingInstructions();

        var dataItems = ops.Outbound.OfType<MultiplexedData>().ToList();
        Assert.NotEmpty(dataItems);
    }
}
