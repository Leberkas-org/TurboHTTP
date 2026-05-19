namespace TurboHTTP.Routing;

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

    public TurboRouteHandlerBuilder AllowAnonymous()
    {
        Metadata.AllowsAnonymous = true;
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
}

public sealed record ProducesMetadata(Type Type, int StatusCode);
public sealed record ProducesProblemMetadata(int StatusCode);
