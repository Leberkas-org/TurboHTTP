using System.Text.Json;
using TurboHTTP.Context;
using TurboHTTP.Server;

namespace TurboHTTP.Routing.Binding;

internal sealed class JsonBodyBinder(Type type) : ParameterBinder
{
    public override async ValueTask<object?> BindAsync(TurboHttpContext ctx, IServiceProvider services)
    {
        var req = ctx.Request as TurboHttpRequest;
        if (req?.Content is null)
        {
            return null;
        }

        await using var stream = await req.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, type);
    }
}