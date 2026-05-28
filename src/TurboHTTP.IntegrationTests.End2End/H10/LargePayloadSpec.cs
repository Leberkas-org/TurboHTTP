using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using TurboHTTP.IntegrationTests.End2End.Shared;

namespace TurboHTTP.IntegrationTests.End2End.H10;

[Collection("H10")]
public sealed class LargePayloadSpec : End2EndSpecBase
{
    protected override Version ProtocolVersion => HttpVersion.Version10;

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

        app.MapGet("/generate", async (int size, HttpContext ctx) =>
        {
            ctx.Response.ContentType = "application/octet-stream";
            var buffer = new byte[1024];
            Array.Fill(buffer, (byte)0xAB);
            var remaining = size;
            while (remaining > 0)
            {
                var toWrite = Math.Min(1024, remaining);
                await ctx.Response.Body.WriteAsync(buffer.AsMemory(0, toWrite), CancellationToken);
                remaining -= toWrite;
            }
        });

        app.MapPost("/empty-echo", async ctx =>
        {
            using var stream = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(stream, CancellationToken);
            var length = stream.Length;
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync(length.ToString(), CancellationToken);
        });
    }

    [Fact(Timeout = 30000)]
    public async Task LargePayload_should_roundtrip_body_over_64kb()
    {
        var payload = new byte[128 * 1024];
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
    public async Task LargePayload_should_receive_large_server_response()
    {
        var size = 256 * 1024;
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/generate?size={size}");

        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBytes = await response.Content.ReadAsByteArrayAsync(CancellationToken);
        Assert.Equal(size, responseBytes.Length);
        Assert.True(responseBytes.All(b => b == 0xAB));
    }

    [Fact(Timeout = 30000)]
    public async Task LargePayload_should_handle_empty_body()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUri}/empty-echo")
        {
            Content = new ByteArrayContent([])
        };

        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Equal("0", body);
    }
}
