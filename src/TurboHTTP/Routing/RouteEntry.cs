using Microsoft.AspNetCore.Routing;

namespace TurboHTTP.Routing;

internal sealed class RouteEntry
{
    public string Method { get; }
    public string Pattern { get; }
    public string[] Segments { get; }
    public IRouteDispatcher Dispatcher { get; }
    public bool IsStatic { get; }

    public RouteEntry(string method, string pattern, IRouteDispatcher dispatcher)
    {
        Method = method;
        Pattern = pattern;
        Segments = pattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
        Dispatcher = dispatcher;
        IsStatic = !Array.Exists(Segments, s => s.StartsWith('{'));
    }

    public bool TryMatch(string method, ReadOnlySpan<char> path, RouteValueDictionary routeValues)
    {
        if (Method != "*" && !string.Equals(Method, method, StringComparison.OrdinalIgnoreCase))
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

    public bool IsStaticMatch(ReadOnlySpan<char> path)
    {
        if (path.Length > 0 && path[0] == '/')
        {
            path = path[1..];
        }

        var segmentIndex = 0;
        while (path.Length > 0)
        {
            if (segmentIndex >= Segments.Length)
            {
                return false;
            }

            var template = Segments[segmentIndex];
            if (template.StartsWith('{'))
            {
                return false;
            }

            int slashPos = path.IndexOf('/');
            var segment = slashPos < 0 ? path : path[..slashPos];

            if (!segment.Equals(template, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            segmentIndex++;
            path = slashPos < 0 ? default : path[(slashPos + 1)..];
        }

        return segmentIndex == Segments.Length;
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
