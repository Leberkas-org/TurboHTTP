using Microsoft.AspNetCore.Http;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Routing;

public sealed class TurboEntityAskBuilderSpec
{
    private sealed record OrderDto(string Id);

    private sealed record NotFoundResult;

    [Fact(Timeout = 5000)]
    public void Mappers_should_be_empty_by_default()
    {
        var builder = new TurboEntityAskBuilder();
        Assert.Equal(0, builder.Mappers.Count);
    }

    [Fact(Timeout = 5000)]
    public void Response_should_register_mapper()
    {
        var builder = new TurboEntityAskBuilder();
        builder.Handle<OrderDto>((_, _) => Task.CompletedTask);
        Assert.Equal(1, builder.Mappers.Count);
    }

    [Fact(Timeout = 5000)]
    public async Task Response_should_invoke_handler_with_typed_response()
    {
        var capturedId = "";
        var builder = new TurboEntityAskBuilder();
        builder.Handle<OrderDto>((_, order) =>
        {
            capturedId = order.Id;
            return Task.CompletedTask;
        });

        var mapper = builder.Mappers.FindMapper(typeof(OrderDto));
        Assert.NotNull(mapper);
        await mapper(null!, new OrderDto("42"));
        Assert.Equal("42", capturedId);
    }

    [Fact(Timeout = 5000)]
    public void Produces_should_register_mapper()
    {
        var builder = new TurboEntityAskBuilder();
        builder.Produces<OrderDto>((_, _) => new TestResult(200));
        Assert.Equal(1, builder.Mappers.Count);
    }

    [Fact(Timeout = 5000)]
    public async Task Produces_should_execute_iresult()
    {
        var resultExecuted = false;
        var builder = new TurboEntityAskBuilder();
        builder.Produces<OrderDto>((_, _) =>
        {
            resultExecuted = true;
            return new TestResult(200);
        });

        var mapper = builder.Mappers.FindMapper(typeof(OrderDto));
        Assert.NotNull(mapper);

        var ctx = ServerTestContext.Request().Get("/")
            .RequestAborted(TestContext.Current.CancellationToken).Build();
        await mapper(ctx, new OrderDto("1"));
        Assert.True(resultExecuted);
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void Response_and_Produces_should_coexist()
    {
        var builder = new TurboEntityAskBuilder();
        builder.Handle<OrderDto>((_, _) => Task.CompletedTask);
        builder.Produces<NotFoundResult>((_, _) => new TestResult(404));

        Assert.Equal(2, builder.Mappers.Count);
        Assert.NotNull(builder.Mappers.FindMapper(typeof(OrderDto)));
        Assert.NotNull(builder.Mappers.FindMapper(typeof(NotFoundResult)));
    }

    [Fact(Timeout = 5000)]
    public void WithTimeout_should_set_override()
    {
        var builder = new TurboEntityAskBuilder();
        builder.WithTimeout(TimeSpan.FromSeconds(42));
        Assert.Equal(TimeSpan.FromSeconds(42), builder.TimeoutOverride);
    }

    [Fact(Timeout = 5000)]
    public void TimeoutOverride_should_be_null_by_default()
    {
        var builder = new TurboEntityAskBuilder();
        Assert.Null(builder.TimeoutOverride);
    }

    private sealed class TestResult(int statusCode) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }
    }
}
