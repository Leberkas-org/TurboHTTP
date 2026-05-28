using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using TurboHTTP.IntegrationTests.End2End.Shared;
using Xunit;

namespace TurboHTTP.IntegrationTests.End2End.H11;

[Collection("H11")]
public sealed class RoundtripSpec : End2EndSpecBase
{
    protected override Version ProtocolVersion => HttpVersion.Version11;

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/hello", () => Results.Ok("Hello World"));

        app.MapPost("/echo", async (HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync(CancellationToken);
            return Results.Ok(body);
        });

        app.MapPut("/put-echo", async (HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync(CancellationToken);
            return Results.Ok(body);
        });

        app.MapDelete("/delete-me", () => Results.NoContent());

        app.MapGet("/headers", (HttpContext ctx) =>
        {
            var customHeader = ctx.Request.Headers["X-Custom-Header"].ToString();
            var response = new { header = customHeader };
            ctx.Response.Headers["X-Echo-Header"] = customHeader;
            return Results.Ok(response);
        });
    }

    [Fact(Timeout = 15000)]
    public async Task Roundtrip_should_return_200_for_get()
    {
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
    public async Task Roundtrip_should_echo_put_body()
    {
        var payload = "test put payload";
        var request = new HttpRequestMessage(HttpMethod.Put, $"{BaseUri}/put-echo")
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
    public async Task Roundtrip_should_return_204_for_delete()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUri}/delete-me");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Roundtrip_should_return_404_for_unknown_route()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/nonexistent");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Roundtrip_should_echo_custom_headers()
    {
        var headerValue = "custom-test-value";
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/headers");
        request.Headers.Add("X-Custom-Header", headerValue);

        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.NotEmpty(body);
        Assert.True(response.Headers.TryGetValues("X-Echo-Header", out var values));
        Assert.Contains(headerValue, values);
    }
}
