using System.Net;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using TurboHttp.Transport;

namespace TurboHttp.StreamTests.IO;

/// <summary>
/// Tests <see cref="ClientRunner"/> async connection behaviour introduced by TASK-017.
/// Verifies that TCP connect and TLS handshake use async paths so actor threads are not blocked.
/// </summary>
public sealed class ClientRunnerTests : TestKit
{
    private sealed class FakeClientProvider(Task<Stream> streamTask, EndPoint remoteEndPoint) : IClientProvider
    {
        public EndPoint? RemoteEndPoint => remoteEndPoint;

        public Task<Stream> GetStreamAsync(CancellationToken ct = default) => streamTask;

        public void Close() { }
    }

    [Fact(DisplayName = "TASK-017-001: ClientConnected sent to handler when GetStreamAsync succeeds")]
    public void Should_SendClientConnected_WhenGetStreamAsyncSucceeds()
    {
        var remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 9999);
        Stream stream = new MemoryStream();
        var provider = new FakeClientProvider(Task.FromResult(stream), remoteEndPoint);
        var handlerProbe = CreateTestProbe("handler");

        var runner = Sys.ActorOf(
            Props.Create(() => new ClientRunner(provider, handlerProbe.Ref, 65536)),
            "runner");

        var connected = handlerProbe.ExpectMsg<ClientRunner.ClientConnected>(TimeSpan.FromSeconds(5));
        Assert.Equal(remoteEndPoint, connected.RemoteEndPoint);
        Assert.NotNull(connected.InboundReader);
        Assert.NotNull(connected.OutboundWriter);

        Sys.Stop(runner);
    }

    [Fact(DisplayName = "TASK-017-002: ClientDisconnected sent to handler when GetStreamAsync fails")]
    public void Should_SendClientDisconnected_WhenGetStreamAsyncFails()
    {
        var remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 9999);
        var failedTask = Task.FromException<Stream>(new InvalidOperationException("Connection refused"));
        var provider = new FakeClientProvider(failedTask, remoteEndPoint);
        var handlerProbe = CreateTestProbe("handler");

        Sys.ActorOf(
            Props.Create(() => new ClientRunner(provider, handlerProbe.Ref, 65536)),
            "runner-fail");

        handlerProbe.ExpectMsg<ClientRunner.ClientDisconnected>(TimeSpan.FromSeconds(5));
    }
}
