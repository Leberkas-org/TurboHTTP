using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Servus.Akka.Transport;

namespace TurboHTTP.Server;

public sealed class TurboHttpsOptions
{
    public X509Certificate2? ServerCertificate { get; set; }
    public string? CertificatePath { get; set; }
    public string? CertificatePassword { get; set; }
    public SslProtocols EnabledSslProtocols { get; set; } = SslProtocols.None;
    public RemoteCertificateValidationCallback? ClientCertificateValidationCallback { get; set; }
    public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public ClientCertificateMode ClientCertificateMode { get; set; } = ClientCertificateMode.NoCertificate;
    public Func<string?, X509Certificate2?>? ServerCertificateSelector { get; set; }
}