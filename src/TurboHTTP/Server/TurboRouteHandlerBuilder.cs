using TurboHTTP.Routing;

namespace TurboHTTP.Server;


public sealed class TurboRouteHandlerBuilder
{
    public EndpointMetadata Metadata { get; } = new();

    public TurboRouteHandlerBuilder WithName(string name)
    {
        Metadata.Name = name;
        return this;
    }

    public TurboRouteHandlerBuilder WithTags(params string[] tags)
    {
        Metadata.Tags.AddRange(tags);
        return this;
    }

    public TurboRouteHandlerBuilder WithMetadata(params object[] metadata)
    {
        Metadata.Items.AddRange(metadata);
        return this;
    }

    public TurboRouteHandlerBuilder RequireAuthorization()
    {
        Metadata.RequiresAuthorization = true;
        return this;
    }

    public TurboRouteHandlerBuilder RequireAuthorization(string? policy)
    {
        Metadata.AuthorizationPolicies.Add(policy);
        return this;
    }

    public TurboRouteHandlerBuilder AllowAnonymous()
    {
        Metadata.AllowsAnonymous = true;
        return this;
    }

    public TurboRouteHandlerBuilder WithDisplayName(string displayName)
    {
        Metadata.DisplayName = displayName;
        return this;
    }

    public TurboRouteHandlerBuilder Produces<T>(int statusCode = 200)
    {
        Metadata.Items.Add(new ProducesMetadata(typeof(T), statusCode));
        return this;
    }

    public TurboRouteHandlerBuilder ProducesProblem(int statusCode = 500)
    {
        Metadata.Items.Add(new ProducesProblemMetadata(statusCode));
        return this;
    }

    internal TurboEndpointMetadata? BuildMetadata()
    {
        if (Metadata.Items.Count == 0 && Metadata.Tags.Count == 0 &&
            !Metadata.RequiresAuthorization && !Metadata.AllowsAnonymous &&
            Metadata.AuthorizationPolicies.Count == 0 &&
            Metadata.Name is null && Metadata.DisplayName is null)
        {
            return null;
        }

        var items = new List<object>(Metadata.Items);

        if (Metadata.Tags.Count > 0)
        {
            items.Add(new TagsMetadata(Metadata.Tags.ToArray()));
        }

        foreach (var policy in Metadata.AuthorizationPolicies)
        {
            items.Add(new AuthorizeData(policy, null, null));
        }

        if (Metadata.RequiresAuthorization && Metadata.AuthorizationPolicies.Count == 0)
        {
            items.Add(new AuthorizeData(null, null, null));
        }

        if (Metadata.AllowsAnonymous)
        {
            items.Add(new AllowAnonymousMarker());
        }

        return new TurboEndpointMetadata(items);
    }
}

public sealed record ProducesMetadata(Type Type, int StatusCode);
public sealed record ProducesProblemMetadata(int StatusCode);
