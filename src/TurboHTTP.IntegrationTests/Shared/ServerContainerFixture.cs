using System.Net;
using System.Net.Quic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace TurboHTTP.IntegrationTests.Shared;

public sealed class ServerContainerFixture : IAsyncLifetime
{
    private const int HttpBinPort = 8080;
    private const int NginxInternalPort = 443;
    private const string HttpBinAlias = "httpbin";
    private const string NetworkName = "turbohttp-v2";
    private const string HttpBinContainerName = "turbohttp-httpbin";
    private const string NginxH2ContainerName = "turbohttp-nginx-h2";
    private const string NginxH3ContainerName = "turbohttp-nginx-h3";

    private static string BuildNginxConf(int listenPort) => $$"""
        events {}
        http {
            upstream backend {
                server {{HttpBinAlias}}:{{HttpBinPort}};
            }
            server {
                listen {{listenPort}} ssl;
                listen {{listenPort}} quic reuseport;
                http2 on;

                ssl_certificate     /etc/nginx/ssl/cert.pem;
                ssl_certificate_key /etc/nginx/ssl/key.pem;
                ssl_protocols       TLSv1.2 TLSv1.3;

                add_header Alt-Svc 'h3=":{{listenPort}}"; ma=86400' always;

                location / {
                    proxy_pass http://backend;
                    proxy_set_header Host $host;
                    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
                }
            }
        }
        """;

    private INetwork? _network;
    private IContainer? _httpBin;
    private IContainer? _nginxH2;
    private IContainer? _nginxH3;
    private string? _tempDir;

    public int HttpPort { get; private set; }

    public int HttpsPort { get; private set; }

    public int QuicPort { get; private set; }

    public bool IsQuicAvailable { get; private set; }

    public bool IsDockerAvailable { get; private set; }

    public async ValueTask InitializeAsync()
    {
        if (!await ProbeDockerAsync())
        {
            return;
        }

        IsDockerAvailable = true;

        await RemoveStaleResourcesAsync();

        _network = new NetworkBuilder()
            .WithName(NetworkName)
            .Build();

        await _network.CreateAsync();

        _httpBin = new ContainerBuilder("mccutchen/go-httpbin:v2.15.0")
            .WithName(HttpBinContainerName)
            .WithNetwork(_network)
            .WithNetworkAliases(HttpBinAlias)
            .WithPortBinding(HttpBinPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r
                    .ForPort(HttpBinPort)
                    .ForPath("/get")))
            .Build();

        await _httpBin.StartAsync();
        HttpPort = _httpBin.GetMappedPublicPort(HttpBinPort);

        PrepareNginxFiles();
        await StartNginxH2Async();
        await StartNginxH3Async();
    }

    private void PrepareNginxFiles()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "turbohttp-nginx-ssl");

        if (Directory.Exists(_tempDir) &&
            File.Exists(Path.Combine(_tempDir, "ssl", "cert.pem")) &&
            File.Exists(Path.Combine(_tempDir, "ssl", "key.pem")))
        {
            return;
        }

        Directory.CreateDirectory(Path.Combine(_tempDir, "ssl"));

        var (certPem, keyPem) = GenerateSelfSignedCert();
        File.WriteAllText(Path.Combine(_tempDir, "ssl", "cert.pem"), certPem);
        File.WriteAllText(Path.Combine(_tempDir, "ssl", "key.pem"), keyPem);
    }

    private async Task StartNginxH2Async()
    {
        var h2Dir = Path.Combine(_tempDir!, "h2");
        Directory.CreateDirectory(h2Dir);
        var confPath = Path.Combine(h2Dir, "nginx.conf");
        await File.WriteAllTextAsync(confPath, BuildNginxConf(NginxInternalPort));

        try
        {
            _nginxH2 = new ContainerBuilder("macbre/nginx-http3:latest")
                .WithName(NginxH2ContainerName)
                .WithNetwork(_network!)
                .WithPortBinding(NginxInternalPort, true)
                .WithResourceMapping(new FileInfo(confPath), "/etc/nginx/")
                .WithResourceMapping(new DirectoryInfo(Path.Combine(_tempDir!, "ssl")), "/etc/nginx/ssl/")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(NginxInternalPort))
                .Build();

            await _nginxH2.StartAsync();
            HttpsPort = _nginxH2.GetMappedPublicPort(NginxInternalPort);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[ServerContainerFixture] nginx-h2 failed: {ex.Message}");
        }
    }

    private async Task StartNginxH3Async()
    {
        if (!QuicConnection.IsSupported)
        {
            return;
        }

        var port = GetFreePort();
        var h3Dir = Path.Combine(_tempDir!, "h3");
        Directory.CreateDirectory(h3Dir);
        var confPath = Path.Combine(h3Dir, "nginx.conf");
        await File.WriteAllTextAsync(confPath, BuildNginxConf(port));

        try
        {
            _nginxH3 = new ContainerBuilder("macbre/nginx-http3:latest")
                .WithName(NginxH3ContainerName)
                .WithNetwork(_network!)
                .WithPortBinding(port, port)
                .WithResourceMapping(new FileInfo(confPath), "/etc/nginx/")
                .WithResourceMapping(new DirectoryInfo(Path.Combine(_tempDir!, "ssl")), "/etc/nginx/ssl/")
                .Build();

            await _nginxH3.StartAsync();
            QuicPort = port;
            IsQuicAvailable = await ProbeQuicAsync(port);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[ServerContainerFixture] nginx-h3 failed: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_nginxH3 is not null)
        {
            await _nginxH3.DisposeAsync();
        }

        if (_nginxH2 is not null)
        {
            await _nginxH2.DisposeAsync();
        }

        if (_httpBin is not null)
        {
            await _httpBin.DisposeAsync();
        }

        if (_network is not null)
        {
            await _network.DisposeAsync();
        }
    }

    private static async Task RemoveStaleResourcesAsync()
    {
        var containerNames = new[] { NginxH3ContainerName, NginxH2ContainerName, HttpBinContainerName };
        foreach (var name in containerNames)
        {
            await RunDockerQuietAsync($"rm -f {name}");
        }

        await RunDockerQuietAsync($"network rm {NetworkName}");
    }

    private static async Task RunDockerQuietAsync(string arguments)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is not null)
            {
                await process.WaitForExitAsync(cts.Token);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static (string CertPem, string KeyPem) GenerateSelfSignedCert()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=localhost",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        san.AddIpAddress(IPAddress.IPv6Loopback);
        san.AddDnsName("localhost");
        req.CertificateExtensions.Add(san.Build());

        var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(365));

        return (cert.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem());
    }

    private static async Task<bool> ProbeDockerAsync()
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

            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync(cts.Token);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> ProbeQuicAsync(int port)
    {
        if (!QuicConnection.IsSupported)
        {
            return false;
        }

        try
        {
            using var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            using var client = new HttpClient(handler);
            client.DefaultRequestVersion = HttpVersion.Version30;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await client.GetAsync($"https://localhost:{port}/get", cts.Token);
            return response.IsSuccessStatusCode && response.Version == HttpVersion.Version30;
        }
        catch
        {
            return false;
        }
    }
}
