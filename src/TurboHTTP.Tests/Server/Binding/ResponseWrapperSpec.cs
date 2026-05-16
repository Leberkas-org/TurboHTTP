using System.Net;
using TurboHTTP.Server.Binding;

namespace TurboHTTP.Tests.Server.Binding;

public sealed class ResponseWrapperSpec
{
    [Fact(Timeout = 5000)]
    public async Task Wrap_HttpResponseMessage_should_passthrough()
    {
        var wrapper = ResponseWrapper.CreateWrapper(typeof(HttpResponseMessage));
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var result = await wrapper(response);
        Assert.Same(response, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Wrap_string_should_return_200_text()
    {
        var wrapper = ResponseWrapper.CreateWrapper(typeof(string));
        var result = await wrapper("hello");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("hello", await result.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("text/plain", result.Content.Headers.ContentType?.MediaType);
    }

    [Fact(Timeout = 5000)]
    public async Task Wrap_null_should_return_204()
    {
        var wrapper = ResponseWrapper.CreateWrapper(typeof(object));
        var result = await wrapper(null);
        Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Wrap_object_should_return_json()
    {
        var wrapper = ResponseWrapper.CreateWrapper(typeof(object));
        var result = await wrapper(new { Name = "test" });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("application/json", result.Content.Headers.ContentType?.MediaType);
        var body = await result.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("test", body);
    }

    [Fact(Timeout = 5000)]
    public async Task Wrap_void_should_return_204()
    {
        var wrapper = ResponseWrapper.CreateVoidWrapper();
        var result = await wrapper(null);
        Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
    }
}