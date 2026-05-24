using Microsoft.AspNetCore.Routing;

namespace TurboHTTP.Routing;

public sealed class RouteMatchResult
{
    internal static readonly RouteValueDictionary EmptyRouteValues = new();
    public static readonly RouteMatchResult NoMatch = new(false, null, EmptyRouteValues);

    public bool IsMatch { get; }
    internal IRouteDispatcher? Dispatcher { get; }
    public RouteValueDictionary RouteValues { get; }

    internal RouteMatchResult(bool isMatch, IRouteDispatcher? dispatcher, RouteValueDictionary routeValues)
    {
        IsMatch = isMatch;
        Dispatcher = dispatcher;
        RouteValues = routeValues;
    }
}

public sealed class RouteTable
{
    private readonly Dictionary<string, IRouteDispatcher> _staticRoutes;
    private readonly Dictionary<string, RouteEntry[]> _parameterizedByMethod;
    private readonly RouteEntry[] _wildcardParameterized;

    internal RouteTable(RouteEntry[] entries)
    {
        var staticRoutes = new Dictionary<string, IRouteDispatcher>(StringComparer.OrdinalIgnoreCase);
        var paramByMethod = new Dictionary<string, List<RouteEntry>>(StringComparer.OrdinalIgnoreCase);
        var wildcardParam = new List<RouteEntry>();

        foreach (var entry in entries)
        {
            if (entry.IsStatic)
            {
                var key = string.Concat(entry.Method, " ", entry.Pattern);
                staticRoutes.TryAdd(key, entry.Dispatcher);

                if (entry.Method == "*")
                {
                    staticRoutes.TryAdd(string.Concat("GET ", entry.Pattern), entry.Dispatcher);
                    staticRoutes.TryAdd(string.Concat("POST ", entry.Pattern), entry.Dispatcher);
                    staticRoutes.TryAdd(string.Concat("PUT ", entry.Pattern), entry.Dispatcher);
                    staticRoutes.TryAdd(string.Concat("DELETE ", entry.Pattern), entry.Dispatcher);
                    staticRoutes.TryAdd(string.Concat("PATCH ", entry.Pattern), entry.Dispatcher);
                    staticRoutes.TryAdd(string.Concat("HEAD ", entry.Pattern), entry.Dispatcher);
                    staticRoutes.TryAdd(string.Concat("OPTIONS ", entry.Pattern), entry.Dispatcher);
                }
            }
            else if (entry.Method == "*")
            {
                wildcardParam.Add(entry);
            }
            else
            {
                if (!paramByMethod.TryGetValue(entry.Method, out var list))
                {
                    list = [];
                    paramByMethod[entry.Method] = list;
                }
                list.Add(entry);
            }
        }

        _staticRoutes = staticRoutes;
        _parameterizedByMethod = new Dictionary<string, RouteEntry[]>(paramByMethod.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in paramByMethod)
        {
            _parameterizedByMethod[kv.Key] = kv.Value.ToArray();
        }
        _wildcardParameterized = wildcardParam.ToArray();
    }

    public RouteMatchResult Match(string method, string path)
    {
        var key = string.Concat(method, " ", path);
        if (_staticRoutes.TryGetValue(key, out var dispatcher))
        {
            return new RouteMatchResult(true, dispatcher, RouteMatchResult.EmptyRouteValues);
        }

        if (_parameterizedByMethod.TryGetValue(method, out var methodEntries))
        {
            foreach (var entry in methodEntries)
            {
                var routeValues = new RouteValueDictionary();
                if (entry.TryMatch(method, path, routeValues))
                {
                    return new RouteMatchResult(true, entry.Dispatcher, routeValues);
                }
            }
        }

        foreach (var entry in _wildcardParameterized)
        {
            var routeValues = new RouteValueDictionary();
            if (entry.TryMatch(method, path, routeValues))
            {
                return new RouteMatchResult(true, entry.Dispatcher, routeValues);
            }
        }

        return RouteMatchResult.NoMatch;
    }
}

internal sealed class RouteTableBuilder
{
    private readonly List<RouteEntry> _entries = [];

    public RouteTableBuilder Add(string method, string pattern, IRouteDispatcher dispatcher)
    {
        _entries.Add(new RouteEntry(method, pattern, dispatcher));
        return this;
    }

    public RouteTable Build()
    {
        return new RouteTable(_entries.ToArray());
    }
}
