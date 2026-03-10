namespace TurboHttp.Client;

public interface ITurboHttpClientFactory
{
    ITurboHttpClient CreateClient();
}