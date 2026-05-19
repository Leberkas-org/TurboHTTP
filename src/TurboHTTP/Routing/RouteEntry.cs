using Microsoft.AspNetCore.Routing;

namespace TurboHTTP.Routing;

internal sealed class RouteEntry
{
    public HttpMethod Method { get; }
    public string Pattern { get; }
    public string[] Segments { get; }
    public IRouteDispatcher Dispatcher { get; }

    public RouteEntry(HttpMethod method, string pattern, IRouteDispatcher dispatcher)
    {
        Method = method;
        Pattern = pattern;
        Segments = pattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
        Dispatcher = dispatcher;
    }

    public bool TryMatch(HttpMethod method, ReadOnlySpan<char> path, RouteValueDictionary routeValues)
    {
        if (Method.Method != "*" && !Method.Equals(method))
        {
            return false;
        }

        var pathSegments = SplitPath(path);
        if (pathSegments.Length != Segments.Length)
        {
            return false;
        }

        for (var i = 0; i < Segments.Length; i++)
        {
            var template = Segments[i];
            var actual = pathSegments[i];

            if (template.StartsWith('{') && template.EndsWith('}'))
            {
                var paramName = template[1..^1];
                routeValues[paramName] = actual;
            }
            else if (!string.Equals(template, actual, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public bool IsStaticMatch(string[] pathSegments)
    {
        if (pathSegments.Length != Segments.Length)
        {
            return false;
        }

        for (var i = 0; i < Segments.Length; i++)
        {
            if (Segments[i].StartsWith('{'))
            {
                return false;
            }

            if (!string.Equals(Segments[i], pathSegments[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] SplitPath(ReadOnlySpan<char> path)
    {
        if (path.Length > 0 && path[0] == '/')
        {
            path = path[1..];
        }

        if (path.Length == 0)
        {
            return [];
        }

        return path.ToString().Split('/');
    }
}
