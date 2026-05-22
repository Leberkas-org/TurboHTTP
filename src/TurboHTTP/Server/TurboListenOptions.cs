using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace TurboHTTP.Server;

public sealed class TurboListenOptions(IPAddress address, ushort port)
{
    public IPAddress Address { get; } = address;
    public ushort Port { get; } = port;
    public HttpProtocols Protocols { get; set; } = HttpProtocols.Http1AndHttp2;

    internal bool IsHttps => HttpsOptions is not null;
    internal TurboHttpsOptions? HttpsOptions { get; private set; }

    public void UseHttps()
    {
        HttpsOptions = new TurboHttpsOptions();
    }

    public void UseHttps(X509Certificate2 certificate)
    {
        HttpsOptions = new TurboHttpsOptions { ServerCertificate = certificate };
    }

    public void UseHttps(string path, string? password = null)
    {
        HttpsOptions = new TurboHttpsOptions
        {
            CertificatePath = path,
            CertificatePassword = password
        };
    }

    public void UseHttps(Action<TurboHttpsOptions> configure)
    {
        HttpsOptions = new TurboHttpsOptions();
        configure(HttpsOptions);
    }

    public void UseHttps(X509Certificate2 certificate, Action<TurboHttpsOptions> configure)
    {
        HttpsOptions = new TurboHttpsOptions { ServerCertificate = certificate };
        configure(HttpsOptions);
    }

    public void UseHttps(string path, string? password, Action<TurboHttpsOptions> configure)
    {
        HttpsOptions = new TurboHttpsOptions
        {
            CertificatePath = path,
            CertificatePassword = password
        };
        configure(HttpsOptions);
    }

    internal string? ConnectionLoggingCategory { get; private set; }

    public void UseConnectionLogging()
    {
        ConnectionLoggingCategory = "TurboHTTP.Server.ConnectionLogging";
    }

    public void UseConnectionLogging(string loggerName)
    {
        ConnectionLoggingCategory = loggerName;
    }
}