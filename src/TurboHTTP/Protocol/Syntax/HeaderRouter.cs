using Microsoft.AspNetCore.Http;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.Syntax;

internal static class HeaderRouter
{
    public static void ApplyToRequest(HttpRequestMessage message, HeaderCollection parsed)
    {
        foreach (var entry in parsed)
        {
            if (ContentHeaderClassifier.IsContentHeader(entry.Name))
            {
                message.Content?.Headers.TryAddWithoutValidation(entry.Name, entry.Value);
            }
            else
            {
                message.Headers.TryAddWithoutValidation(entry.Name, entry.Value);
            }
        }
    }

    public static void ApplyToResponse(HttpResponseMessage message, HeaderCollection parsed)
    {
        foreach (var entry in parsed)
        {
            if (ContentHeaderClassifier.IsContentHeader(entry.Name))
            {
                message.Content?.Headers.TryAddWithoutValidation(entry.Name, entry.Value);
            }
            else
            {
                message.Headers.TryAddWithoutValidation(entry.Name, entry.Value);
            }
        }
    }

    public static void ApplyToHeaderDictionary(IHeaderDictionary target, HeaderCollection parsed)
    {
        foreach (var entry in parsed)
        {
            if (target.TryGetValue(entry.Name, out var existing))
            {
                target[entry.Name] = Microsoft.Extensions.Primitives.StringValues.Concat(existing, entry.Value);
            }
            else
            {
                target[entry.Name] = entry.Value;
            }
        }
    }
}