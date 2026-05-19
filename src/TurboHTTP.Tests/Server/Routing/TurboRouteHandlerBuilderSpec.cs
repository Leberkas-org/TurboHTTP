using TurboHTTP.Routing;

namespace TurboHTTP.Tests.Server.Routing;

public sealed class TurboRouteHandlerBuilderSpec
{
    [Fact(Timeout = 5000)]
    public void WithName_should_store_name()
    {
        var builder = new TurboRouteHandlerBuilder();
        var result = builder.WithName("GetUsers");
        Assert.Same(builder, result);
        Assert.Equal("GetUsers", builder.Metadata.Name);
    }

    [Fact(Timeout = 5000)]
    public void WithTags_should_store_tags()
    {
        var builder = new TurboRouteHandlerBuilder();
        builder.WithTags("users", "admin");
        Assert.Equal(new[] { "users", "admin" }, builder.Metadata.Tags);
    }

    [Fact(Timeout = 5000)]
    public void WithMetadata_should_store_arbitrary_metadata()
    {
        var builder = new TurboRouteHandlerBuilder();
        builder.WithMetadata("key1", 42);
        Assert.Contains("key1", builder.Metadata.Items);
        Assert.Contains(42, builder.Metadata.Items);
    }

    [Fact(Timeout = 5000)]
    public void RequireAuthorization_should_set_flag()
    {
        var builder = new TurboRouteHandlerBuilder();
        builder.RequireAuthorization();
        Assert.True(builder.Metadata.RequiresAuthorization);
    }

    [Fact(Timeout = 5000)]
    public void AllowAnonymous_should_set_flag()
    {
        var builder = new TurboRouteHandlerBuilder();
        builder.AllowAnonymous();
        Assert.True(builder.Metadata.AllowsAnonymous);
    }

    [Fact(Timeout = 5000)]
    public void Fluent_chaining_should_return_same_instance()
    {
        var builder = new TurboRouteHandlerBuilder();
        var result = builder
            .WithName("test")
            .WithTags("t1")
            .RequireAuthorization();
        Assert.Same(builder, result);
    }
}
