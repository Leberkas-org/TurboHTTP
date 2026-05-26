namespace TurboHTTP.Routing;

public sealed class EndpointMetadata
{
    public string? Name { get; internal set; }
    public string? DisplayName { get; internal set; }
    public List<string> Tags { get; } = [];
    public List<object> Items { get; } = [];
    public List<string?> AuthorizationPolicies { get; } = [];
    public bool RequiresAuthorization { get; internal set; }
    public bool AllowsAnonymous { get; internal set; }
}
