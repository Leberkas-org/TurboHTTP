using Akka.Streams.Dsl;
using TurboHttp.Client;
using TurboHttp.IO;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Streams;

public sealed class HostRoutingStageOptionsTests : StreamTestBase
{
    private sealed class FakePool : IHostConnectionPool
    {
        public void Send(HttpRequestMessage _)
        {
            // noop
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private async Task MaterializeAndPushAsync(HostRoutingStage stage, params HttpRequestMessage[] requests)
    {
        await Source.From(requests)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Ignore<HttpResponseMessage>(), Materializer);

        // Give Akka stream processing time to complete
        await Task.Delay(500);
    }

    // ── tests ──────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "HRS-001: http URI → pool created with TcpOptions (not TlsOptions)")]
    public async Task HRS_001_HttpUri_CreatesTcpOptions()
    {
        var capturedOptions = new List<TcpOptions>();
        var options = new TurboClientOptions { ConnectTimeout = TimeSpan.FromSeconds(20) };
        var stage = new HostRoutingStage(options);
        stage.PoolFactory = (opts, sys, cb) =>
        {
            capturedOptions.Add(opts);
            return new FakePool();
        };

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        await MaterializeAndPushAsync(stage, request);

        Assert.Single(capturedOptions);
        Assert.NotNull(capturedOptions[0]);
        // TlsOptions extends TcpOptions, so check exact type
        Assert.Equal(typeof(TcpOptions), capturedOptions[0].GetType());
    }

    [Fact(Timeout = 10_000, DisplayName = "HRS-002: https URI → pool created with TlsOptions")]
    public async Task HRS_002_HttpsUri_CreatesTlsOptions()
    {
        var capturedOptions = new List<TcpOptions>();
        var options = new TurboClientOptions { ConnectTimeout = TimeSpan.FromSeconds(20) };
        var stage = new HostRoutingStage(options);
        stage.PoolFactory = (opts, sys, cb) =>
        {
            capturedOptions.Add(opts);
            return new FakePool();
        };

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        await MaterializeAndPushAsync(stage, request);

        Assert.Single(capturedOptions);
        Assert.IsType<TlsOptions>(capturedOptions[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "HRS-003: clientOptions.ConnectTimeout=20s → resulting TcpOptions.ConnectTimeout == 20s")]
    public async Task HRS_003_ClientOptionsConnectTimeoutPropagated()
    {
        var capturedOptions = new List<TcpOptions>();
        var timeout = TimeSpan.FromSeconds(20);
        var options = new TurboClientOptions { ConnectTimeout = timeout };
        var stage = new HostRoutingStage(options);
        stage.PoolFactory = (opts, sys, cb) =>
        {
            capturedOptions.Add(opts);
            return new FakePool();
        };

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        await MaterializeAndPushAsync(stage, request);

        Assert.Single(capturedOptions);
        Assert.Equal(timeout, capturedOptions[0].ConnectTimeout);
    }

    [Fact(Timeout = 10_000, DisplayName = "HRS-004: Two requests to same host:port:scheme → same pool reused (no second creation)")]
    public async Task HRS_004_SameHostPortScheme_ReusesPool()
    {
        var capturedOptions = new List<TcpOptions>();
        var options = new TurboClientOptions { ConnectTimeout = TimeSpan.FromSeconds(20) };
        var stage = new HostRoutingStage(options);
        stage.PoolFactory = (opts, sys, cb) =>
        {
            capturedOptions.Add(opts);
            return new FakePool();
        };

        var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path1");
        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path2");

        await MaterializeAndPushAsync(stage, request1, request2);

        Assert.Single(capturedOptions);
    }

    [Fact(Timeout = 10_000, DisplayName = "HRS-005: Two requests to different host → two separate pools")]
    public async Task HRS_005_DifferentHosts_CreatesSeparatePools()
    {
        var capturedOptions = new List<TcpOptions>();
        var options = new TurboClientOptions { ConnectTimeout = TimeSpan.FromSeconds(20) };
        var stage = new HostRoutingStage(options);
        stage.PoolFactory = (opts, sys, cb) =>
        {
            capturedOptions.Add(opts);
            return new FakePool();
        };

        var request1 = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://b.test/");

        await MaterializeAndPushAsync(stage, request1, request2);

        Assert.Equal(2, capturedOptions.Count);
    }

    [Fact(Timeout = 10_000, DisplayName = "HRS-006: http://a.test and https://a.test → two separate pools (different scheme)")]
    public async Task HRS_006_SameHostDifferentScheme_CreatesSeparatePools()
    {
        var capturedOptions = new List<TcpOptions>();
        var options = new TurboClientOptions { ConnectTimeout = TimeSpan.FromSeconds(20) };
        var stage = new HostRoutingStage(options);
        stage.PoolFactory = (opts, sys, cb) =>
        {
            capturedOptions.Add(opts);
            return new FakePool();
        };

        var request1 = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        var request2 = new HttpRequestMessage(HttpMethod.Get, "https://a.test/");

        await MaterializeAndPushAsync(stage, request1, request2);

        Assert.Equal(2, capturedOptions.Count);
    }
}
