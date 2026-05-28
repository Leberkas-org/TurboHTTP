using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Servus.Akka.AspNetCore.Tests;

public sealed class EntityBuilderSpec
{
    [Fact(Timeout = 5000)]
    public void OnGet_should_register_GET_method()
    {
        var builder = new EntityBuilder();
        builder.OnGet(() => new object());

        Assert.True(builder.Methods.ContainsKey("GET"));
    }

    [Fact(Timeout = 5000)]
    public void OnPost_should_register_POST_method()
    {
        var builder = new EntityBuilder();
        builder.OnPost(() => new object());

        Assert.True(builder.Methods.ContainsKey("POST"));
    }

    [Fact(Timeout = 5000)]
    public void OnPut_should_register_PUT_method()
    {
        var builder = new EntityBuilder();
        builder.OnPut(() => new object());

        Assert.True(builder.Methods.ContainsKey("PUT"));
    }

    [Fact(Timeout = 5000)]
    public void OnDelete_should_register_DELETE_method()
    {
        var builder = new EntityBuilder();
        builder.OnDelete(() => new object());

        Assert.True(builder.Methods.ContainsKey("DELETE"));
    }

    [Fact(Timeout = 5000)]
    public void OnPatch_should_register_PATCH_method()
    {
        var builder = new EntityBuilder();
        builder.OnPatch(() => new object());

        Assert.True(builder.Methods.ContainsKey("PATCH"));
    }

    [Fact(Timeout = 5000)]
    public void Multiple_methods_should_register_independently()
    {
        var builder = new EntityBuilder();
        builder.OnGet(() => new object());
        builder.OnPost(() => new object());
        builder.OnDelete(() => new object());

        Assert.Equal(3, builder.Methods.Count);
        Assert.True(builder.Methods.ContainsKey("GET"));
        Assert.True(builder.Methods.ContainsKey("POST"));
        Assert.True(builder.Methods.ContainsKey("DELETE"));
    }

    [Fact(Timeout = 5000)]
    public void WithTimeout_should_set_timeout()
    {
        var builder = new EntityBuilder();
        builder.WithTimeout(TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(30), builder.Timeout);
    }

    [Fact(Timeout = 5000)]
    public void Default_timeout_should_be_5_seconds()
    {
        var builder = new EntityBuilder();

        Assert.Equal(TimeSpan.FromSeconds(5), builder.Timeout);
    }

    [Fact(Timeout = 5000)]
    public void WithTimeout_should_be_fluent()
    {
        var builder = new EntityBuilder();
        var result = builder.WithTimeout(TimeSpan.FromSeconds(10));

        Assert.Same(builder, result);
    }

    [Fact(Timeout = 5000)]
    public void Ask_should_configure_method_as_ask()
    {
        var builder = new EntityBuilder();
        builder.OnGet(() => new object()).Ask(ask =>
        {
            ask.Handle<string>(async (ctx, resp) =>
            {
                await ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(resp));
            });
        });

        var config = builder.Methods["GET"].ToConfig();
        Assert.False(config.IsTell);
        Assert.NotNull(config.EndpointMappers);
    }

    [Fact(Timeout = 5000)]
    public void Tell_should_configure_method_as_tell()
    {
        var builder = new EntityBuilder();
        builder.OnPost(() => new object()).Tell(tell => { tell.Produces(HttpStatusCode.Accepted); });

        var config = builder.Methods["POST"].ToConfig();
        Assert.True(config.IsTell);
        Assert.NotNull(config.TellResponseHandler);
    }

    [Fact(Timeout = 5000)]
    public void Tell_without_config_should_default_to_no_handler()
    {
        var builder = new EntityBuilder();
        builder.OnPost(() => new object()).Tell();

        var config = builder.Methods["POST"].ToConfig();
        Assert.True(config.IsTell);
        Assert.Null(config.TellResponseHandler);
    }

    [Fact(Timeout = 5000)]
    public void Response_should_add_mapper_to_builder()
    {
        var builder = new EntityBuilder();
        builder.Response<string>(async (ctx, resp) =>
        {
            await ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(resp));
        });

        Assert.Equal(1, builder.ResponseMappers.Count);
    }

    [Fact(Timeout = 5000)]
    public void Response_should_be_fluent()
    {
        var builder = new EntityBuilder();
        var result = builder.Response<string>(async (ctx, resp) =>
        {
            await ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(resp));
        });

        Assert.Same(builder, result);
    }

    [Fact(Timeout = 5000)]
    public void Methods_should_case_insensitive()
    {
        var builder = new EntityBuilder();
        builder.OnGet(() => new object());

        Assert.True(builder.Methods.ContainsKey("get"));
        Assert.True(builder.Methods.ContainsKey("GET"));
        Assert.True(builder.Methods.ContainsKey("Get"));
    }
}