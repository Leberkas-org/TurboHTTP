using TurboHTTP.Protocol.Multiplexed;

namespace TurboHTTP.Tests.Protocol.Multiplexed;

public sealed class ReconnectionManagerSpec
{
    private static HttpRequestMessage Get(string url) => new(HttpMethod.Get, url);

    [Fact(Timeout = 5000)]
    public void ReconnectionManager_should_buffer_replayable_requests()
    {
        var mgr = new ReconnectionManager(maxAttempts: 3);
        var replayable = new List<HttpRequestMessage>
        {
            Get("http://host/a"),
            Get("http://host/b")
        };

        mgr.OnConnectionLost(replayable);

        Assert.True(mgr.IsReconnecting);
        Assert.Equal(2, mgr.BufferedCount);
    }

    [Fact(Timeout = 5000)]
    public void ReconnectionManager_should_return_buffered_requests_on_restore()
    {
        var mgr = new ReconnectionManager(maxAttempts: 3);
        var req1 = Get("http://host/a");
        var req2 = Get("http://host/b");

        mgr.OnConnectionLost([req1, req2]);
        var restored = mgr.OnConnectionRestored();

        Assert.Equal(2, restored.Count);
        Assert.Contains(req1, restored);
        Assert.Contains(req2, restored);
        Assert.False(mgr.IsReconnecting);
        Assert.Equal(0, mgr.BufferedCount);
    }

    [Fact(Timeout = 5000)]
    public void ReconnectionManager_should_respect_max_retry_attempts()
    {
        var mgr = new ReconnectionManager(maxAttempts: 3);

        mgr.OnConnectionLost([Get("http://host/a")]);
        Assert.True(mgr.OnReconnectAttemptFailed());
        Assert.True(mgr.OnReconnectAttemptFailed());
        Assert.False(mgr.OnReconnectAttemptFailed());
        Assert.False(mgr.IsReconnecting);
    }

    [Fact(Timeout = 5000)]
    public void ReconnectionManager_should_reset_attempts_on_restore()
    {
        var mgr = new ReconnectionManager(maxAttempts: 3);

        mgr.OnConnectionLost([Get("http://host/a")]);
        mgr.OnReconnectAttemptFailed();
        mgr.OnConnectionRestored();

        Assert.False(mgr.IsReconnecting);

        mgr.OnConnectionLost([Get("http://host/b")]);
        Assert.True(mgr.OnReconnectAttemptFailed());
        Assert.True(mgr.OnReconnectAttemptFailed());
    }

    [Fact(Timeout = 5000)]
    public void ReconnectionManager_should_handle_empty_replayable_list()
    {
        var mgr = new ReconnectionManager(maxAttempts: 3);

        mgr.OnConnectionLost([]);

        Assert.True(mgr.IsReconnecting);
        Assert.Equal(0, mgr.BufferedCount);
    }

    [Fact(Timeout = 5000)]
    public void ReconnectionManager_should_reset_state()
    {
        var mgr = new ReconnectionManager(maxAttempts: 3);
        mgr.OnConnectionLost([Get("http://host/a")]);

        mgr.Reset();

        Assert.False(mgr.IsReconnecting);
        Assert.Equal(0, mgr.BufferedCount);
    }
}