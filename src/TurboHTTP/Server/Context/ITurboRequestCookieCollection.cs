namespace TurboHTTP.Server.Context;

public interface ITurboRequestCookieCollection : IEnumerable<KeyValuePair<string, string>>
{
    string? this[string key] { get; }
    int Count { get; }
    ICollection<string> Keys { get; }
    bool ContainsKey(string key);
}
