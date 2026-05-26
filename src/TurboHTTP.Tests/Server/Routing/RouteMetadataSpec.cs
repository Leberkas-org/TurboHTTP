using TurboHTTP.Routing;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server.Routing;

public sealed class RouteMetadataSpec
{
    [Fact(Timeout = 5000)]
    public void RouteHandlerBuilder_RequireAuthorization_with_policy_should_add_authorize_data()
    {
        var builder = new TurboRouteHandlerBuilder();
        builder.RequireAuthorization("Admin");

        var metadata = builder.BuildMetadata();
        Assert.NotNull(metadata);

        var authData = metadata.GetMetadata<IAuthorizeData>();
        Assert.NotNull(authData);
        Assert.Equal("Admin", authData.Policy);
    }

    [Fact(Timeout = 5000)]
    public void RouteHandlerBuilder_RequireAuthorization_with_null_policy_should_add_authorize_data()
    {
        var builder = new TurboRouteHandlerBuilder();
        builder.RequireAuthorization(null);

        var metadata = builder.BuildMetadata();
        Assert.NotNull(metadata);

        var authData = metadata.GetMetadata<IAuthorizeData>();
        Assert.NotNull(authData);
        Assert.Null(authData.Policy);
    }

    [Fact(Timeout = 5000)]
    public void RouteHandlerBuilder_RequireAuthorization_multiple_policies_should_accumulate()
    {
        var builder = new TurboRouteHandlerBuilder();
        builder.RequireAuthorization("Policy1");
        builder.RequireAuthorization("Policy2");

        var metadata = builder.BuildMetadata();
        Assert.NotNull(metadata);

        var authData = metadata.GetOrderedMetadata<IAuthorizeData>().ToList();
        Assert.Equal(2, authData.Count);
        Assert.Equal("Policy1", authData[0].Policy);
        Assert.Equal("Policy2", authData[1].Policy);
    }

    [Fact(Timeout = 5000)]
    public void RouteHandlerBuilder_WithDisplayName_should_set_display_name()
    {
        var builder = new TurboRouteHandlerBuilder();
        builder.WithDisplayName("Get Users");

        Assert.Equal("Get Users", builder.Metadata.DisplayName);
    }

    [Fact(Timeout = 5000)]
    public void RouteHandlerBuilder_WithTags_should_create_tags_metadata()
    {
        var builder = new TurboRouteHandlerBuilder();
        builder.WithTags("api", "v2");

        var metadata = builder.BuildMetadata();
        Assert.NotNull(metadata);

        var tags = metadata.GetMetadata<ITagsMetadata>();
        Assert.NotNull(tags);
        Assert.Equal(2, tags.Tags.Count);
        Assert.Equal("api", tags.Tags[0]);
        Assert.Equal("v2", tags.Tags[1]);
    }

    [Fact(Timeout = 5000)]
    public void RouteHandlerBuilder_AllowAnonymous_should_add_marker()
    {
        var builder = new TurboRouteHandlerBuilder();
        builder.AllowAnonymous();

        var metadata = builder.BuildMetadata();
        Assert.NotNull(metadata);
        Assert.True(metadata.HasMetadata<IAllowAnonymous>());
    }

    [Fact(Timeout = 5000)]
    public void RouteHandlerBuilder_WithMetadata_should_preserve_custom_objects()
    {
        var custom = new CustomMeta("value");
        var builder = new TurboRouteHandlerBuilder();
        builder.WithMetadata(custom);

        var metadata = builder.BuildMetadata();
        Assert.NotNull(metadata);

        var retrieved = metadata.GetMetadata<CustomMeta>();
        Assert.Same(custom, retrieved);
    }

    [Fact(Timeout = 5000)]
    public void RouteHandlerBuilder_empty_should_return_null_metadata()
    {
        var builder = new TurboRouteHandlerBuilder();
        var metadata = builder.BuildMetadata();
        Assert.Null(metadata);
    }

    [Fact(Timeout = 5000)]
    public void RouteHandlerBuilder_legacy_RequireAuthorization_without_policy_should_work()
    {
        var builder = new TurboRouteHandlerBuilder();
        builder.RequireAuthorization();

        var metadata = builder.BuildMetadata();
        Assert.NotNull(metadata);

        var authData = metadata.GetMetadata<IAuthorizeData>();
        Assert.NotNull(authData);
        Assert.Null(authData.Policy);
    }

    [Fact(Timeout = 5000)]
    public void RouteHandlerBuilder_RequireAuthorization_without_policy_followed_by_with_policy_uses_only_policy()
    {
        var builder = new TurboRouteHandlerBuilder();
        builder.RequireAuthorization();
        builder.RequireAuthorization("Admin");

        var metadata = builder.BuildMetadata();
        Assert.NotNull(metadata);

        var authData = metadata.GetOrderedMetadata<IAuthorizeData>().ToList();
        Assert.Single(authData);
        Assert.Equal("Admin", authData[0].Policy);
    }

    [Fact(Timeout = 5000)]
    public void RouteGroupBuilder_WithTags_should_apply_to_routes()
    {
        var table = new TurboRouteTable();
        var group = table.CreateGroup("/api");
        group.WithTags("api", "v1");

        var handler = group.MapGet("/users", () => Microsoft.AspNetCore.Http.Results.Ok());
        var metadata = handler.BuildMetadata();

        Assert.NotNull(metadata);
        var tags = metadata.GetMetadata<ITagsMetadata>();
        Assert.NotNull(tags);
        Assert.Equal(2, tags.Tags.Count);
    }

    [Fact(Timeout = 5000)]
    public void RouteGroupBuilder_RequireAuthorization_with_policy_should_apply_to_routes()
    {
        var table = new TurboRouteTable();
        var group = table.CreateGroup("/api");
        group.RequireAuthorization("Admin");

        var handler = group.MapGet("/users", () => Microsoft.AspNetCore.Http.Results.Ok());
        var metadata = handler.BuildMetadata();

        Assert.NotNull(metadata);
        var authData = metadata.GetMetadata<IAuthorizeData>();
        Assert.NotNull(authData);
        Assert.Equal("Admin", authData.Policy);
    }

    [Fact(Timeout = 5000)]
    public void RouteGroupBuilder_RequireAuthorization_without_policy_should_apply_to_routes()
    {
        var table = new TurboRouteTable();
        var group = table.CreateGroup("/api");
        group.RequireAuthorization();

        var handler = group.MapGet("/users", () => Microsoft.AspNetCore.Http.Results.Ok());
        var metadata = handler.BuildMetadata();

        Assert.NotNull(metadata);
        var authData = metadata.GetMetadata<IAuthorizeData>();
        Assert.NotNull(authData);
        Assert.Null(authData.Policy);
    }

    [Fact(Timeout = 5000)]
    public void RouteGroupBuilder_AllowAnonymous_should_apply_to_routes()
    {
        var table = new TurboRouteTable();
        var group = table.CreateGroup("/api");
        group.AllowAnonymous();

        var handler = group.MapGet("/users", () => Microsoft.AspNetCore.Http.Results.Ok());
        var metadata = handler.BuildMetadata();

        Assert.NotNull(metadata);
        Assert.True(metadata.HasMetadata<IAllowAnonymous>());
    }

    [Fact(Timeout = 5000)]
    public void RouteGroupBuilder_WithMetadata_should_apply_to_routes()
    {
        var custom = new CustomMeta("group-value");
        var table = new TurboRouteTable();
        var group = table.CreateGroup("/api");
        group.WithMetadata(custom);

        var handler = group.MapGet("/users", () => Microsoft.AspNetCore.Http.Results.Ok());
        var metadata = handler.BuildMetadata();

        Assert.NotNull(metadata);
        var retrieved = metadata.GetMetadata<CustomMeta>();
        Assert.Same(custom, retrieved);
    }

    [Fact(Timeout = 5000)]
    public void RouteGroupBuilder_multiple_WithTags_should_accumulate()
    {
        var table = new TurboRouteTable();
        var group = table.CreateGroup("/api");
        group.WithTags("tag1");
        group.WithTags("tag2");

        var handler = group.MapGet("/users", () => Microsoft.AspNetCore.Http.Results.Ok());
        var metadata = handler.BuildMetadata();

        Assert.NotNull(metadata);
        var tags = metadata.GetMetadata<ITagsMetadata>();
        Assert.NotNull(tags);
        Assert.Equal(2, tags.Tags.Count);
    }

    [Fact(Timeout = 5000)]
    public void RouteGroupBuilder_group_and_route_metadata_should_merge()
    {
        var table = new TurboRouteTable();
        var group = table.CreateGroup("/api");
        group.WithTags("group-tag");
        group.RequireAuthorization("GroupPolicy");

        var handler = group.MapGet("/users", () => Microsoft.AspNetCore.Http.Results.Ok());
        handler.RequireAuthorization("RoutePolicy");

        var metadata = handler.BuildMetadata();
        Assert.NotNull(metadata);

        var authData = metadata.GetOrderedMetadata<IAuthorizeData>().ToList();
        Assert.Equal(2, authData.Count);
        Assert.Equal("GroupPolicy", authData[0].Policy);
        Assert.Equal("RoutePolicy", authData[1].Policy);

        var tags = metadata.GetMetadata<ITagsMetadata>();
        Assert.NotNull(tags);
        Assert.Single(tags.Tags);
        Assert.Equal("group-tag", tags.Tags[0]);
    }

    [Fact(Timeout = 5000)]
    public void RouteGroupBuilder_nested_groups_should_merge_metadata()
    {
        var table = new TurboRouteTable();
        var group1 = table.CreateGroup("/api");
        group1.WithTags("api");
        group1.RequireAuthorization("Admin");

        var group2 = group1.MapGroup("/v1");
        group2.WithTags("v1");

        var handler = group2.MapGet("/users", () => Microsoft.AspNetCore.Http.Results.Ok());
        var metadata = handler.BuildMetadata();

        Assert.NotNull(metadata);

        var authData = metadata.GetMetadata<IAuthorizeData>();
        Assert.NotNull(authData);
        Assert.Equal("Admin", authData.Policy);
    }

    private sealed record CustomMeta(string Value);
}
