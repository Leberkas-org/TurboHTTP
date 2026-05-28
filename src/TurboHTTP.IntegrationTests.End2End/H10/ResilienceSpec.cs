using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using TurboHTTP.IntegrationTests.End2End.Shared;
using Xunit;

namespace TurboHTTP.IntegrationTests.End2End.H10;

public sealed class ResilienceSpec : End2EndSpecBase
{
    private TaskCompletionSource _handlerGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override Version ProtocolVersion => HttpVersion.Version10;

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/fast", () => Results.Ok("ok"));

        app.MapGet("/slow", async () =>
        {
            await Task.Delay(30000, CancellationToken);
            return Results.Ok("done");
        });

        app.MapGet("/blocked", async () =>
        {
            await _handlerGate.Task;
            return Results.Ok("unblocked");
        });
    }

    public override async ValueTask DisposeAsync()
    {
        _handlerGate.TrySetResult();
        await base.DisposeAsync();
    }

    [Fact(Timeout = 15000)]
    public async Task Resilience_should_complete_fast_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/fast");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Equal("ok", body);
    }

    [Fact(Timeout = 15000)]
    public async Task Resilience_should_timeout_slow_request()
    {
        var originalTimeout = Client.Timeout;
        try
        {
            Client.Timeout = TimeSpan.FromSeconds(1);

            var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/slow");

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await Client.SendAsync(request, CancellationToken));
        }
        finally
        {
            Client.Timeout = originalTimeout;
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Resilience_should_cancel_via_cancellation_token()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/slow");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await Client.SendAsync(request, cts.Token));
    }
}
