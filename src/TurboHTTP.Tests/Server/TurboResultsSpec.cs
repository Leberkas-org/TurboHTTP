using System.Net;
using System.Text.Json;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server;

public sealed class TurboResultsSpec
{
    [Fact(Timeout = 5000)]
    public void Ok_should_return_200()
    {
        var response = TurboResults.Ok();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Ok_with_value_should_return_200_with_json()
    {
        var response = TurboResults.Ok(new { Name = "test" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var doc = JsonDocument.Parse(body);
        Assert.Equal("test", doc.RootElement.GetProperty("Name").GetString());
    }

    [Fact(Timeout = 5000)]
    public void NotFound_should_return_404()
    {
        var response = TurboResults.NotFound();
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void BadRequest_should_return_400()
    {
        var response = TurboResults.BadRequest();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void NoContent_should_return_204()
    {
        var response = TurboResults.NoContent();
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Created_should_return_201_with_json()
    {
        var response = TurboResults.Created(new { Id = 42 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("42", body);
    }

    [Fact(Timeout = 5000)]
    public void Json_should_serialize_with_custom_status()
    {
        var response = TurboResults.Json(new { Ok = true }, HttpStatusCode.Accepted);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }
}