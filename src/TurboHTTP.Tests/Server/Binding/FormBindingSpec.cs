using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server;
using TurboHTTP.Server.Binding;

namespace TurboHTTP.Tests.Server.Binding;

public sealed class FormBindingSpec
{
    [Fact(Timeout = 5000)]
    public async Task FormBinder_should_extract_urlencoded_value()
    {
        var binder = new FormBinder("name", typeof(string));
        var ctx = CreateUrlEncodedContext("/submit", "name=Alice&age=30");
        var result = await binder.BindAsync(ctx, null!);
        Assert.Equal("Alice", result);
    }

    [Fact(Timeout = 5000)]
    public async Task FormBinder_should_parse_int_from_urlencoded()
    {
        var binder = new FormBinder("age", typeof(int));
        var ctx = CreateUrlEncodedContext("/submit", "name=Alice&age=30");
        var result = await binder.BindAsync(ctx, null!);
        Assert.Equal(30, result);
    }

    [Fact(Timeout = 5000)]
    public async Task FormBinder_should_return_null_when_key_missing()
    {
        var binder = new FormBinder("missing", typeof(string));
        var ctx = CreateUrlEncodedContext("/submit", "name=Alice");
        var result = await binder.BindAsync(ctx, null!);
        Assert.Null(result);
    }

    [Fact(Timeout = 5000)]
    public async Task FormFileBinder_should_extract_file_from_multipart()
    {
        var binder = new FormFileBinder("file");
        var ctx = CreateMultipartContext("/upload", "file", "test.txt", "hello world"u8.ToArray());
        var result = await binder.BindAsync(ctx, null!);
        var file = Assert.IsAssignableFrom<IFormFile>(result);
        Assert.Equal("test.txt", file.FileName);
        Assert.Equal(11, file.Length);
    }

    [Fact(Timeout = 5000)]
    public async Task FromForm_attribute_should_bind_in_handler()
    {
        var captured = "";
        Delegate handler = ([FromForm] string name, [FromForm] int age) =>
        {
            captured = string.Concat(name, "-", age);
            return TypedResults.Ok("success");
        };
        var bound = DelegateHandlerBinder.Bind("/submit", handler);
        var ctx = CreateUrlEncodedContext("/submit", "name=Alice&age=30");
        var services = CreateServiceProvider();
        ctx.RequestServices = services;
        await bound(ctx, services);
        Assert.Equal(200, ctx.Response.StatusCode);
        Assert.Equal("Alice-30", captured);
    }

    [Fact(Timeout = 5000)]
    public void FromBody_and_FromForm_should_be_mutually_exclusive()
    {
        Delegate handler = ([FromBody] string a, [FromForm] string b) => TypedResults.Ok();
        Assert.Throws<InvalidOperationException>(() => DelegateHandlerBinder.Bind("/test", handler));
    }

    private static IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddProblemDetails();
        return services.BuildServiceProvider();
    }

    private static TurboHttpContext CreateUrlEncodedContext(string path, string formData)
    {
        var content = new StringContent(formData, Encoding.UTF8, "application/x-www-form-urlencoded");
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost" + path)
        {
            Content = content
        };
        var connection = new TurboConnectionInfo("test", null, 0, null, 0);
        return TestContextFactory.Create(request: request, connection: connection);
    }

    private static TurboHttpContext CreateMultipartContext(
        string path, string fieldName, string fileName, byte[] fileContent)
    {
        var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(fileContent), fieldName, fileName);
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost" + path)
        {
            Content = multipart
        };
        var connection = new TurboConnectionInfo("test", null, 0, null, 0);
        return TestContextFactory.Create(request: request, connection: connection);
    }
}
