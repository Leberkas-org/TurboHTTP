using System.Net;
using System.Net.Quic;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using TurboHTTP.IntegrationTests.End2End.Shared;
using Xunit;

namespace TurboHTTP.IntegrationTests.End2End.H3;

public sealed class MultiplexingSpec : End2EndSpecBase
{
    protected override Version ProtocolVersion => HttpVersion.Version30;

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/id/{id}", (int id) => Results.Ok(id));

        app.MapGet("/delay/{ms}", async (int ms) =>
        {
            await Task.Delay(ms, CancellationToken);
            return Results.Ok(ms);
        });
    }

    [Fact(Timeout = 30000)]
    public async Task Multiplexing_should_handle_parallel_streams()
    {
        if (!QuicConnection.IsSupported)
        {
            return;
        }

        var tasks = new Task<int>[20];
        for (int i = 0; i < 20; i++)
        {
            var id = i;
            tasks[i] = Task.Run(async () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/id/{id}");
                var response = await Client.SendAsync(request, CancellationToken);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync(CancellationToken);
                var value = JsonSerializer.Deserialize<int>(body);
                return value;
            });
        }

        var results = await Task.WhenAll(tasks);

        var distinctResults = results.Distinct().ToArray();
        Assert.Equal(20, distinctResults.Length);
    }

    [Fact(Timeout = 30000)]
    public async Task Multiplexing_should_not_starve_fast_streams()
    {
        if (!QuicConnection.IsSupported)
        {
            return;
        }

        var slowTask = Task.Run(async () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/delay/2000");
            var response = await Client.SendAsync(request, CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(CancellationToken);
            return JsonSerializer.Deserialize<int>(body);
        });

        await Task.Delay(100);

        var fastStart = DateTime.UtcNow;
        var fastRequest = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/id/42");
        var fastResponse = await Client.SendAsync(fastRequest, CancellationToken);
        var fastElapsed = DateTime.UtcNow - fastStart;

        Assert.Equal(HttpStatusCode.OK, fastResponse.StatusCode);
        var fastBody = await fastResponse.Content.ReadAsStringAsync(CancellationToken);
        var fastValue = JsonSerializer.Deserialize<int>(fastBody);
        Assert.Equal(42, fastValue);

        Assert.True(fastElapsed < TimeSpan.FromSeconds(1), $"Fast request took {fastElapsed.TotalMilliseconds}ms");

        await slowTask;
    }
}
