using System.Buffers;
using System.Threading.Channels;
using TurboHttp.Transport;

namespace TurboHttp.Tests.Transport;

/// <summary>
/// Tests <see cref="ClientState.Dispose"/> — verifies that pending
/// <see cref="IMemoryOwner{T}"/> items in both channels are disposed during cleanup.
/// </summary>
public sealed class ClientStateTests
{
    private sealed class TrackingMemoryOwner(int size) : IMemoryOwner<byte>
    {
        private readonly byte[] _data = new byte[size];
        public bool Disposed { get; private set; }

        public Memory<byte> Memory => _data.AsMemory();

        public void Dispose() => Disposed = true;
    }

    [Fact(DisplayName = "TASK-019-001: DisposeAsync drains inbound channel and disposes IMemoryOwner<byte> items")]
    public void Should_DisposeInboundItems_WhenDisposeAsyncCalled()
    {
        // Arrange: pre-populate inbound channel with two tracking owners
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var outbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var stream = new MemoryStream();

        var state = new ClientState(65536, stream, inbound, outbound);

        var owner1 = new TrackingMemoryOwner(64);
        var owner2 = new TrackingMemoryOwner(128);
        state.InboundWriter.TryWrite((owner1, 64));
        state.InboundWriter.TryWrite((owner2, 128));

        // Act
        state.Dispose();

        // Assert: both inbound owners must have been disposed
        Assert.True(owner1.Disposed, "First inbound IMemoryOwner<byte> was not disposed");
        Assert.True(owner2.Disposed, "Second inbound IMemoryOwner<byte> was not disposed");
    }

    [Fact(DisplayName = "TASK-019-002: DisposeAsync drains outbound channel and disposes IMemoryOwner<byte> items")]
    public void Should_DisposeOutboundItems_WhenDisposeAsyncCalled()
    {
        // Arrange: pre-populate outbound channel with one tracking owner
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var outbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var stream = new MemoryStream();

        var state = new ClientState(65536, stream, inbound, outbound);

        var owner = new TrackingMemoryOwner(256);
        state.OutboundWriter.TryWrite((owner, 256));

        // Act
        state.Dispose();

        // Assert: outbound owner must have been disposed
        Assert.True(owner.Disposed, "Outbound IMemoryOwner<byte> was not disposed");
    }
}