using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using TurboHTTP.IntegrationTests.End2End.Shared;
using Xunit;

namespace TurboHTTP.IntegrationTests.End2End.H11;

[Collection("H11")]
public sealed class PipeliningSpec : End2EndSpecBase
{
    protected override Version ProtocolVersion => HttpVersion.Version11;

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/item/{id}", (int id) => Results.Ok(id));
    }

    [Fact(Timeout = 15000)]
    public async Task Pipelining_should_return_correct_responses_for_sequential_requests()
    {
        var responses = new int[5];
        for (int i = 0; i < 5; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/item/{i}");
            var response = await Client.SendAsync(request, CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(CancellationToken);
            var value = JsonSerializer.Deserialize<int>(body);
            responses[i] = value;
        }

        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(i, responses[i]);
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Pipelining_should_handle_concurrent_requests()
    {
        var tasks = new Task<int>[10];
        for (int i = 0; i < 10; i++)
        {
            var id = i;
            tasks[i] = Task.Run(async () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/item/{id}");
                var response = await Client.SendAsync(request, CancellationToken);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync(CancellationToken);
                var value = JsonSerializer.Deserialize<int>(body);
                return value;
            });
        }

        var results = await Task.WhenAll(tasks);

        var distinctResults = results.Distinct().ToArray();
        Assert.Equal(10, distinctResults.Length);
    }
}
