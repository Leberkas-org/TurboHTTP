namespace TurboHTTP.Tests.Client;

public sealed class ExtensionsSpec
{
    [Fact(Timeout = 5000)]
    public void GetResponseAsync_should_attach_pending_request_to_options()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var task = request.GetResponseAsync();

        Assert.True(request.Options.TryGetValue(TcsCorrelation.Key, out var pending));
        Assert.NotNull(pending);
        Assert.True(request.Options.TryGetValue(TcsCorrelation.VersionKey, out _));
        Assert.False(task.IsCompleted);
    }

    [Fact(Timeout = 5000)]
    public async Task GetResponseAsync_should_cancel_on_cancellation_token()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var cts = new CancellationTokenSource();

        var task = request.GetResponseAsync(cts.Token);
        Assert.False(task.IsCompleted);

        await cts.CancelAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.True(task.IsCanceled);
    }

    [Fact(Timeout = 5000)]
    public async Task GetResponseAsync_should_complete_when_result_set()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var task = request.GetResponseAsync();

        request.Options.TryGetValue(TcsCorrelation.Key, out var pending);
        request.Options.TryGetValue(TcsCorrelation.VersionKey, out var version);
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        pending!.TrySetResult(response, version);

        var result = await task;
        Assert.Same(response, result);
    }
}
