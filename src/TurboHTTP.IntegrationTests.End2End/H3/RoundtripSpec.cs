using System.Net;
using System.Net.Quic;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using TurboHTTP.IntegrationTests.End2End.Shared;

namespace TurboHTTP.IntegrationTests.End2End.H3;

[Collection("H3")]
public sealed class RoundtripSpec : End2EndSpecBase
{
    protected override Version ProtocolVersion => HttpVersion.Version30;

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/hello", () => Results.Ok("Hello World"));

        app.MapPost("/echo", async (HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync(CancellationToken);
            return Results.Ok(body);
        });
    }

    [Fact(Timeout = 15000)]
    public async Task Roundtrip_should_return_200_for_get()
    {
        if (!QuicConnection.IsSupported)
        {
            return;
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/hello");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var value = JsonSerializer.Deserialize<string>(body);
        Assert.Equal("Hello World", value);
    }

    [Fact(Timeout = 15000)]
    public async Task Roundtrip_should_echo_post_body()
    {
        if (!QuicConnection.IsSupported)
        {
            return;
        }

        var payload = "test payload";
        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUri}/echo")
        {
            Content = new StringContent(payload)
        };

        var response = await Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var value = JsonSerializer.Deserialize<string>(body);
        Assert.Equal(payload, value);
    }

    [Fact(Timeout = 15000)]
    public async Task Roundtrip_should_return_404_for_unknown_route()
    {
        if (!QuicConnection.IsSupported)
        {
            return;
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/nonexistent");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
