using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using TurboHTTP.IntegrationTests.End2End.Shared;

namespace TurboHTTP.IntegrationTests.End2End.H2;

[Collection("H2")]
public sealed class FlowControlSpec : End2EndSpecBase
{
    protected override Version ProtocolVersion => HttpVersion.Version20;

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapPost("/echo-bytes", async ctx =>
        {
            using var stream = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(stream, CancellationToken);
            var data = stream.ToArray();
            ctx.Response.ContentType = "application/octet-stream";
            await ctx.Response.Body.WriteAsync(data, CancellationToken);
        });

        app.MapGet("/generate-large", async ctx =>
        {
            ctx.Response.ContentType = "application/octet-stream";
            var buffer = new byte[16 * 1024];
            Array.Fill(buffer, (byte)0xCD);
            for (var i = 0; i < 64; i++)
            {
                await ctx.Response.Body.WriteAsync(buffer, CancellationToken);
            }
        });
    }

    [Fact(Timeout = 30000)]
    public async Task FlowControl_should_transfer_large_body_under_backpressure()
    {
        var payload = new byte[512 * 1024];
        RandomNumberGenerator.Fill(payload);

        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUri}/echo-bytes")
        {
            Content = new ByteArrayContent(payload)
        };

        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBytes = await response.Content.ReadAsByteArrayAsync(CancellationToken);
        Assert.Equal(payload, responseBytes);
    }

    [Fact(Timeout = 30000)]
    public async Task FlowControl_should_receive_large_server_generated_response()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/generate-large");

        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBytes = await response.Content.ReadAsByteArrayAsync(CancellationToken);
        var expectedSize = 64 * 16 * 1024;
        Assert.Equal(expectedSize, responseBytes.Length);
        Assert.True(responseBytes.All(b => b == 0xCD));
    }
}
