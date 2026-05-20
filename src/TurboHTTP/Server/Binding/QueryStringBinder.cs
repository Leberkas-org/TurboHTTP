using TurboHTTP.Context;

namespace TurboHTTP.Server.Binding;

internal sealed class QueryStringBinder(string name, Type type) : ParameterBinder
{
    public override ValueTask<object?> BindAsync(TurboHttpContext ctx, IServiceProvider services)
    {
        var req = ctx.Request as TurboHttpRequest;
        var uri = req?.RequestUri;
        if (uri?.Query is not { Length: > 0 } query)
        {
            return ValueTask.FromResult(type.IsValueType ? Activator.CreateInstance(type) : null);
        }

        var pairs = query.TrimStart('?').Split('&');
        foreach (var pair in pairs)
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && string.Equals(kv[0], name, StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult<object?>(RouteValueBinder.ParseValue(Uri.UnescapeDataString(kv[1]), type));
            }
        }

        return ValueTask.FromResult(type.IsValueType ? Activator.CreateInstance(type) : null);
    }
}