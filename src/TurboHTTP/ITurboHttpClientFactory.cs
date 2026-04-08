namespace TurboHTTP;

/// <summary>
/// Creates <see cref="ITurboHttpClient"/> instances with optional per-call configuration overrides.
/// </summary>
public interface ITurboHttpClientFactory
{
    ITurboHttpClient CreateClient(string name);
}