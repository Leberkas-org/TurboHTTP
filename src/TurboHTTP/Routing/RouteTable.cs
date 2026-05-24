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
    private readonly Dictionary<string, Dictionary<string, IRouteDispatcher>> _staticByPath;
    private readonly Dictionary<string, RouteEntry[]> _parameterizedByMethod;
    private readonly RouteEntry[] _wildcardParameterized;

    internal RouteTable(RouteEntry[] entries)
    {
        var staticByPath = new Dictionary<string, Dictionary<string, IRouteDispatcher>>(StringComparer.OrdinalIgnoreCase);
        var paramByMethod = new Dictionary<string, List<RouteEntry>>(StringComparer.OrdinalIgnoreCase);
        var wildcardParam = new List<RouteEntry>();

        foreach (var entry in entries)
        {
            if (entry.IsStatic)
            {
                if (!staticByPath.TryGetValue(entry.Pattern, out var methodMap))
                {
                    methodMap = new Dictionary<string, IRouteDispatcher>(StringComparer.OrdinalIgnoreCase);
                    staticByPath[entry.Pattern] = methodMap;
                }

                if (entry.Method == "*")
                {
                    methodMap.TryAdd("GET", entry.Dispatcher);
                    methodMap.TryAdd("POST", entry.Dispatcher);
                    methodMap.TryAdd("PUT", entry.Dispatcher);
                    methodMap.TryAdd("DELETE", entry.Dispatcher);
                    methodMap.TryAdd("PATCH", entry.Dispatcher);
                    methodMap.TryAdd("HEAD", entry.Dispatcher);
                    methodMap.TryAdd("OPTIONS", entry.Dispatcher);
                }
                else
                {
                    methodMap.TryAdd(entry.Method, entry.Dispatcher);
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

        _staticByPath = staticByPath;
        _parameterizedByMethod = new Dictionary<string, RouteEntry[]>(paramByMethod.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in paramByMethod)
        {
            _parameterizedByMethod[kv.Key] = kv.Value.ToArray();
        }
        _wildcardParameterized = wildcardParam.ToArray();
    }

    public RouteMatchResult Match(string method, string path)
    {
        if (_staticByPath.TryGetValue(path, out var methodMap)
            && methodMap.TryGetValue(method, out var dispatcher))
        {
            return new RouteMatchResult(true, dispatcher, RouteMatchResult.EmptyRouteValues);
        }

        if (_parameterizedByMethod.TryGetValue(method, out var methodEntries))
        {
            var routeValues = new RouteValueDictionary();
            foreach (var entry in methodEntries)
            {
                routeValues.Clear();
                if (entry.TryMatch(method, path, routeValues))
                {
                    return new RouteMatchResult(true, entry.Dispatcher, routeValues);
                }
            }
        }

        {
            var routeValues = new RouteValueDictionary();
            foreach (var entry in _wildcardParameterized)
            {
                routeValues.Clear();
                if (entry.TryMatch(method, path, routeValues))
                {
                    return new RouteMatchResult(true, entry.Dispatcher, routeValues);
                }
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
