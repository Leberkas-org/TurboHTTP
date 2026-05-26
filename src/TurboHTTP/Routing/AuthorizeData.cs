namespace TurboHTTP.Routing;

public sealed record AuthorizeData(
    string? Policy,
    string? Roles,
    string? AuthenticationSchemes) : IAuthorizeData;
