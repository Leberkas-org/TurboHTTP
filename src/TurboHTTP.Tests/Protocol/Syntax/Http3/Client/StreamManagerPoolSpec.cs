using TurboHTTP.Protocol.Syntax.Http3.Client;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client;

public sealed class StreamManagerPoolSpec
{
    [Fact(Timeout = 5000)]
    public void Pool_should_recycle_up_to_256_stream_states()
    {
        var ops = new FakeOps();
        var tableSync = new QpackTableSync(0, 4 * 1024, 100, 4 * 1024);
        var decoder = new Http3ClientDecoder(tableSync, 16 * 1024);
        var mgr = new StreamManager(ops, decoder, tableSync);

        for (var i = 0; i < 256; i++)
        {
            var streamId = (long)(i * 4);
            var state = mgr.GetOrCreateStreamState(streamId);
            Assert.NotNull(state);
        }

        mgr.DrainStreams();

        for (var i = 0; i < 256; i++)
        {
            var streamId = (long)((256 + i) * 4);
            var state = mgr.GetOrCreateStreamState(streamId);
            Assert.NotNull(state);
        }

        mgr.Dispose();
    }
}
