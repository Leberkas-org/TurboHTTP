using System.Text.Json;

namespace TurboHTTP.Tests.Shared;

public static class JsonExtensions
{
    public static string? GetHeaderValue(this JsonElement headers, string name)
    {
        foreach (var prop in headers.EnumerateObject())
        {
            if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return prop.Value.ValueKind == JsonValueKind.Array
                ? prop.Value[0].GetString()
                : prop.Value.GetString();
        }

        return null;
    }
}
