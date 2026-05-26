namespace TurboHTTP.Routing;

public interface IAuthorizeData
{
    string? Policy { get; }
    string? Roles { get; }
    string? AuthenticationSchemes { get; }
}
