using Microsoft.AspNetCore.Http;
using TurboHTTP.Server;
using TurboHTTP.Tests.Server;

namespace TurboHTTP.Tests.Routing;

public sealed class TurboEntityTellBuilderSpec
{
    private sealed class TestResult(int statusCode) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }
    }

    [Fact(Timeout = 5000)]
    public void ResponseHandler_should_be_null_by_default()
    {
        var builder = new TurboEntityTellBuilder();
        Assert.Null(builder.ResponseHandler);
    }

    [Fact(Timeout = 5000)]
    public async Task Response_with_status_code_should_set_status()
    {
        var builder = new TurboEntityTellBuilder();
        builder.Response(204);

        var ctx = TestContextFactory.Create();
        await builder.ResponseHandler!(ctx);

        Assert.Equal(204, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Response_with_writer_should_set_status_and_invoke_writer()
    {
        var writerCalled = false;
        var builder = new TurboEntityTellBuilder();
        builder.Response(202, ctx =>
        {
            writerCalled = true;
            return Task.CompletedTask;
        });

        var ctx = TestContextFactory.Create();
        await builder.ResponseHandler!(ctx);

        Assert.Equal(202, ctx.Response.StatusCode);
        Assert.True(writerCalled);
    }

    [Fact(Timeout = 5000)]
    public async Task Produces_should_execute_iresult()
    {
        var builder = new TurboEntityTellBuilder();
        builder.Produces(ctx => new TestResult(201));

        var ctx = TestContextFactory.Create();
        await builder.ResponseHandler!(ctx);

        Assert.Equal(201, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Last_call_should_win()
    {
        var builder = new TurboEntityTellBuilder();
        builder.Response(204);
        builder.Response(202);

        var ctx = TestContextFactory.Create();
        await builder.ResponseHandler!(ctx);

        Assert.Equal(202, ctx.Response.StatusCode);
    }
}
