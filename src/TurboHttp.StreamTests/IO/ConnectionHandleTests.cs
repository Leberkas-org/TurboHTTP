using System.Buffers;
using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using TurboHttp.IO;
using TurboHttp.IO.Stages;
using Xunit;

namespace TurboHttp.StreamTests.IO;

/// <summary>
/// Unit tests for <see cref="ConnectionHandle"/> mutable state (MaxConcurrentStreams).
/// </summary>
public sealed class ConnectionHandleTests : TestKit
{
    private ConnectionHandle CreateHandle()
    {
        var outbound = Channel.CreateUnbounded<(IMemoryOwner<byte> Buffer, int ReadableBytes)>();
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte> Buffer, int ReadableBytes)>();
        var key = new HostKey
        {
            Host = "localhost",
            Port = 443,
            Scheme = "https",
            Version = HttpVersion.Version20
        };

        return new ConnectionHandle(outbound.Writer, inbound.Reader, key, ActorRefs.Nobody);
    }

    [Fact(DisplayName = "MaxConcurrentStreams defaults to 100")]
    public void MaxConcurrentStreams_DefaultsTo100()
    {
        var handle = CreateHandle();

        Assert.Equal(100, handle.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "UpdateMaxConcurrentStreams sets new value")]
    public void UpdateMaxConcurrentStreams_SetsNewValue()
    {
        var handle = CreateHandle();

        handle.UpdateMaxConcurrentStreams(42);

        Assert.Equal(42, handle.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "MaxConcurrentStreams is not part of record equality")]
    public void MaxConcurrentStreams_DoesNotAffectEquality()
    {
        var outbound = Channel.CreateUnbounded<(IMemoryOwner<byte> Buffer, int ReadableBytes)>();
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte> Buffer, int ReadableBytes)>();
        var key = new HostKey
        {
            Host = "localhost",
            Port = 443,
            Scheme = "https",
            Version = HttpVersion.Version20
        };

        var handle1 = new ConnectionHandle(outbound.Writer, inbound.Reader, key, ActorRefs.Nobody);
        var handle2 = new ConnectionHandle(outbound.Writer, inbound.Reader, key, ActorRefs.Nobody);

        handle1.UpdateMaxConcurrentStreams(1);
        handle2.UpdateMaxConcurrentStreams(999);

        // Records with same constructor args should be equal regardless of volatile field
        Assert.Equal(handle1, handle2);
        Assert.Equal(handle1.GetHashCode(), handle2.GetHashCode());
    }

    [Fact(DisplayName = "Concurrent writes and reads do not throw; value is eventually consistent")]
    public async Task ConcurrentWritesAndReads_DoNotThrow_ValueIsEventuallyConsistent()
    {
        var handle = CreateHandle();
        const int iterations = 10_000;
        const int targetValue = 256;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Writer task: rapidly update the value
        var writerTask = Task.Run(() =>
        {
            for (var i = 1; i <= iterations; i++)
            {
                handle.UpdateMaxConcurrentStreams(i);
            }

            handle.UpdateMaxConcurrentStreams(targetValue);
        }, cts.Token);

        // Reader task: rapidly read the value — should never throw
        var readerTask = Task.Run(() =>
        {
            var lastSeen = 0;
            for (var i = 0; i < iterations; i++)
            {
                var value = handle.MaxConcurrentStreams;
                Assert.True(value > 0, $"Expected positive value but got {value}");
                lastSeen = value;
            }

            return lastSeen;
        }, cts.Token);

        await Task.WhenAll(writerTask, readerTask);

        // After writer completes, the final value should be eventually visible
        Assert.Equal(targetValue, handle.MaxConcurrentStreams);
    }
}
