namespace TurboHTTP.IntegrationTests.Client.Shared;

public sealed class ServerContainerFixture : Xunit.IAsyncLifetime
{
    private ITestBackend? _backend;

    public int HttpPort => _backend?.HttpPort ?? 0;
    public int HttpsPort => _backend?.HttpsPort ?? 0;
    public int QuicPort => _backend?.QuicPort ?? 0;
    public bool IsQuicAvailable => _backend?.IsQuicAvailable ?? false;
    public bool IsHttp10TlsSupported => _backend?.IsHttp10TlsSupported ?? false;
    public bool HasCustomEndpoints => _backend?.HasCustomEndpoints ?? false;
    public bool IsBackendAvailable => _backend is not null;

    public async ValueTask InitializeAsync()
    {
        var mode = Environment.GetEnvironmentVariable("TURBOHTTP_TEST_BACKEND")?.ToLowerInvariant();

        _backend = mode switch
        {
            "docker" => await StartDockerAsync(required: true)
                ?? throw new InvalidOperationException("Docker backend failed to initialize."),
            "kestrel" => new KestrelTestBackend(),
            _ => await StartDockerAsync(required: false) ?? (ITestBackend)new KestrelTestBackend()
        };

        await _backend.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_backend is not null)
        {
            await _backend.DisposeAsync();
        }
    }

    private static async Task<DockerTestBackend?> StartDockerAsync(bool required)
    {
        if (!await ProbeDockerAsync())
        {
            if (required)
            {
                throw new InvalidOperationException(
                    "TURBOHTTP_TEST_BACKEND=docker but Docker is not available.");
            }

            return null;
        }

        return new DockerTestBackend();
    }

    internal static async Task<bool> ProbeDockerAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null) return false;
            await process.WaitForExitAsync(cts.Token);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
