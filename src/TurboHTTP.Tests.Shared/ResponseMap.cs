using System.Net;

namespace TurboHTTP.Tests.Shared;

/// <summary>
/// Fluent builder for mapping request paths to HTTP responses.
/// Used with <see cref="ResponseMapFake"/> to create protocol-level test fakes
/// that operate on <see cref="HttpRequestMessage"/>/<see cref="HttpResponseMessage"/>
/// without byte crafting.
/// </summary>
public sealed class ResponseMap
{
    private readonly List<(Func<HttpRequestMessage, bool> Predicate, Func<HttpRequestMessage, HttpResponseMessage> Factory)> _mappings = [];

    /// <summary>
    /// Maps a request path to a static response with the given status and body.
    /// </summary>
    public ResponseMap On(string path, HttpStatusCode status, string body)
    {
        _mappings.Add((
            req => string.Equals(req.RequestUri?.AbsolutePath, path, StringComparison.OrdinalIgnoreCase),
            _ =>
            {
                var response = new HttpResponseMessage(status)
                {
                    Content = new StringContent(body)
                };
                return response;
            }));
        return this;
    }

    /// <summary>
    /// Maps a request path to a dynamic response produced by the given factory.
    /// </summary>
    public ResponseMap On(string path, Func<HttpRequestMessage, HttpResponseMessage> factory)
    {
        _mappings.Add((
            req => string.Equals(req.RequestUri?.AbsolutePath, path, StringComparison.OrdinalIgnoreCase),
            factory));
        return this;
    }

    /// <summary>
    /// Maps requests matching a custom predicate to a dynamic response.
    /// Supports header manipulation and other request-dependent logic.
    /// </summary>
    public ResponseMap On(Func<HttpRequestMessage, bool> predicate, Func<HttpRequestMessage, HttpResponseMessage> factory)
    {
        _mappings.Add((predicate, factory));
        return this;
    }

    /// <summary>
    /// Resolves a request to a response. Returns a 404 for unmapped paths.
    /// </summary>
    internal HttpResponseMessage Resolve(HttpRequestMessage request)
    {
        foreach (var (predicate, factory) in _mappings)
        {
            if (predicate(request))
            {
                var response = factory(request);
                response.RequestMessage = request;
                return response;
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("Not Found"),
            RequestMessage = request
        };
    }
}
