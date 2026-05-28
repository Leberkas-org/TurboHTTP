using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using TurboHTTP.IntegrationTests.End2End.Shared;
using Xunit;

namespace TurboHTTP.IntegrationTests.End2End.H11;

[Collection("H11")]
public sealed class StreamingSpec : End2EndSpecBase
{
    protected override Version ProtocolVersion => HttpVersion.Version11;

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/stream-chunks", async (HttpContext ctx) =>
        {
            for (int i = 0; i < 5; i++)
            {
                await ctx.Response.WriteAsync($"chunk-{i}\n", CancellationToken);
                await ctx.Response.Body.FlushAsync(CancellationToken);
            }
        });

        app.MapGet("/sse", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/event-stream";
            for (int i = 0; i < 3; i++)
            {
                await ctx.Response.WriteAsync($"data: event-{i}\n\n", CancellationToken);
                await ctx.Response.Body.FlushAsync(CancellationToken);
            }
        });
    }

    [Fact(Timeout = 15000)]
    public async Task Streaming_should_receive_all_chunks()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/stream-chunks");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.Contains("chunk-0", body);
        Assert.Contains("chunk-1", body);
        Assert.Contains("chunk-2", body);
        Assert.Contains("chunk-3", body);
        Assert.Contains("chunk-4", body);
    }

    [Fact(Timeout = 15000)]
    public async Task Streaming_should_receive_sse_events()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/sse");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Content.Headers.ContentType?.MediaType == "text/event-stream");
        var body = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.Contains("event-0", body);
        Assert.Contains("event-1", body);
        Assert.Contains("event-2", body);
    }
}
