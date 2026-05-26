using TurboHTTP.Routing;

namespace TurboHTTP.Tests.Server.Routing;

public sealed class TurboEndpointMetadataSpec
{
    [Fact(Timeout = 5000)]
    public void GetMetadata_should_return_null_when_empty()
    {
        var metadata = new TurboEndpointMetadata([]);
        Assert.Null(metadata.GetMetadata<IAllowAnonymous>());
    }

    [Fact(Timeout = 5000)]
    public void GetMetadata_should_return_matching_item()
    {
        var auth = new AuthorizeData("AdminPolicy", null, null);
        var metadata = new TurboEndpointMetadata([auth]);
        Assert.Same(auth, metadata.GetMetadata<IAuthorizeData>());
    }

    [Fact(Timeout = 5000)]
    public void HasMetadata_should_return_true_when_present()
    {
        var metadata = new TurboEndpointMetadata([new AllowAnonymousMarker()]);
        Assert.True(metadata.HasMetadata<IAllowAnonymous>());
    }

    [Fact(Timeout = 5000)]
    public void HasMetadata_should_return_false_when_absent()
    {
        var metadata = new TurboEndpointMetadata([]);
        Assert.False(metadata.HasMetadata<IAllowAnonymous>());
    }

    [Fact(Timeout = 5000)]
    public void GetOrderedMetadata_should_return_all_matching_in_order()
    {
        var auth1 = new AuthorizeData("Policy1", null, null);
        var auth2 = new AuthorizeData("Policy2", null, null);
        var other = new TagsMetadata(["tag1"]);
        var metadata = new TurboEndpointMetadata([auth1, other, auth2]);

        var result = metadata.GetOrderedMetadata<IAuthorizeData>().ToList();
        Assert.Equal(2, result.Count);
        Assert.Equal("Policy1", result[0].Policy);
        Assert.Equal("Policy2", result[1].Policy);
    }

    [Fact(Timeout = 5000)]
    public void Items_should_expose_all_metadata_objects()
    {
        var items = new object[] { new AuthorizeData("P", null, null), new TagsMetadata(["t"]) };
        var metadata = new TurboEndpointMetadata(items);
        Assert.Equal(2, metadata.Items.Count);
    }

    [Fact(Timeout = 5000)]
    public void Merge_should_combine_group_and_route_metadata()
    {
        var group = new TurboEndpointMetadata([new AuthorizeData("GroupPolicy", null, null)]);
        var route = new TurboEndpointMetadata([new TagsMetadata(["route-tag"])]);

        var merged = TurboEndpointMetadata.Merge(group, route);
        Assert.True(merged.HasMetadata<IAuthorizeData>());
        Assert.True(merged.HasMetadata<ITagsMetadata>());
        Assert.Equal(2, merged.Items.Count);
    }

    [Fact(Timeout = 5000)]
    public void Merge_should_cumulate_authorize_data()
    {
        var group = new TurboEndpointMetadata([new AuthorizeData("P1", null, null)]);
        var route = new TurboEndpointMetadata([new AuthorizeData("P2", null, null)]);

        var merged = TurboEndpointMetadata.Merge(group, route);
        Assert.Equal(2, merged.GetOrderedMetadata<IAuthorizeData>().Count());
    }

    [Fact(Timeout = 5000)]
    public void AllowAnonymous_should_coexist_with_RequireAuthorization()
    {
        var metadata = new TurboEndpointMetadata([
            new AuthorizeData("Policy", null, null),
            new AllowAnonymousMarker()
        ]);

        Assert.True(metadata.HasMetadata<IAllowAnonymous>());
        Assert.True(metadata.HasMetadata<IAuthorizeData>());
    }

    private sealed record AllowAnonymousMarker : IAllowAnonymous;
}
