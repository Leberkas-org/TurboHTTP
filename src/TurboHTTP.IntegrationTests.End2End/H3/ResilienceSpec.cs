using System.Net;
using System.Net.Quic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using TurboHTTP.IntegrationTests.End2End.Shared;

namespace TurboHTTP.IntegrationTests.End2End.H3;

[Collection("H3")]
public sealed class ResilienceSpec : End2EndSpecBase
{
    private readonly TaskCompletionSource _handlerGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override Version ProtocolVersion => HttpVersion.Version30;

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
        if (!QuicConnection.IsSupported)
        {
            return;
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/fast");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Equal("ok", body);
    }

    [Fact(Timeout = 15000, Skip = "Client.Timeout not yet implemented")]
    public async Task Resilience_should_timeout_slow_request()
    {
        await Task.CompletedTask;
    }

    [Fact(Timeout = 15000)]
    public async Task Resilience_should_cancel_via_cancellation_token()
    {
        if (!QuicConnection.IsSupported)
        {
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/slow");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await Client.SendAsync(request, cts.Token));
    }
}
