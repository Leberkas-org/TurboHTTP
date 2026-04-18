using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Http3;

public sealed class Http30CertificateValidationSpec : StreamTestBase
{
    private static X509Certificate2 CreateSelfSignedCert(string commonName, params string[] sanDnsNames)
    {
        using var rsa = RSA.Create(2048);
        var request =
            new CertificateRequest($"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        if (sanDnsNames.Length > 0)
        {
            var sanBuilder = new SubjectAlternativeNameBuilder();
            foreach (var dns in sanDnsNames)
            {
                sanBuilder.AddDnsName(dns);
            }

            request.CertificateExtensions.Add(sanBuilder.Build());
        }

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    public void Http30CertificateValidation_should_cover_hostname_when_san_matches_exactly()
    {
        using var cert = CreateSelfSignedCert("Issuer CA", "example.com");

        Assert.True(ConnectionReuseEvaluator.CoversHostname(cert, "example.com"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    public void Http30CertificateValidation_should_not_cover_hostname_when_san_does_not_match()
    {
        using var cert = CreateSelfSignedCert("Issuer CA", "other.com");

        Assert.False(ConnectionReuseEvaluator.CoversHostname(cert, "example.com"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    public void Http30CertificateValidation_should_cover_subdomain_when_wildcard_san_present()
    {
        using var cert = CreateSelfSignedCert("Issuer CA", "*.example.com");

        Assert.True(ConnectionReuseEvaluator.CoversHostname(cert, "api.example.com"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    public void Http30CertificateValidation_should_not_cover_bare_domain_when_wildcard_san_present()
    {
        using var cert = CreateSelfSignedCert("Issuer CA", "*.example.com");

        Assert.False(ConnectionReuseEvaluator.CoversHostname(cert, "example.com"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    public void Http30CertificateValidation_should_not_cover_nested_subdomain_when_wildcard_san_present()
    {
        using var cert = CreateSelfSignedCert("Issuer CA", "*.example.com");

        Assert.False(ConnectionReuseEvaluator.CoversHostname(cert, "deep.sub.example.com"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    public void Http30CertificateValidation_should_cover_hostname_when_cn_matches_and_no_san()
    {
        using var cert = CreateSelfSignedCert("example.com");

        Assert.True(ConnectionReuseEvaluator.CoversHostname(cert, "example.com"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    public void Http30CertificateValidation_should_not_cover_hostname_when_cn_does_not_match()
    {
        using var cert = CreateSelfSignedCert("other.com");

        Assert.False(ConnectionReuseEvaluator.CoversHostname(cert, "example.com"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    public void Http30CertificateValidation_should_cover_hostname_when_any_san_matches()
    {
        using var cert = CreateSelfSignedCert("Issuer CA", "alpha.com", "beta.com", "example.com");

        Assert.True(ConnectionReuseEvaluator.CoversHostname(cert, "example.com"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    public void Http30CertificateValidation_should_not_cover_hostname_when_no_san_matches()
    {
        using var cert = CreateSelfSignedCert("Issuer CA", "alpha.com", "beta.com");

        Assert.False(ConnectionReuseEvaluator.CoversHostname(cert, "example.com"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    public void Http30CertificateValidation_should_deny_reuse_when_cert_does_not_cover_target_host()
    {
        using var cert = CreateSelfSignedCert("Issuer CA", "origin.com");

        var decision = ConnectionReuseEvaluator.Evaluate(
            connectionScheme: "https",
            connectionHost: "origin.com",
            connectionPort: 443,
            targetScheme: "https",
            targetHost: "other.com",
            targetPort: 443,
            serverCertificate: cert,
            isGoingAway: false);

        Assert.False(decision.CanReuse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    public void Http30CertificateValidation_should_allow_reuse_when_cert_covers_target_host()
    {
        using var cert = CreateSelfSignedCert("Issuer CA", "origin.com", "other.com");

        var decision = ConnectionReuseEvaluator.Evaluate(
            connectionScheme: "https",
            connectionHost: "origin.com",
            connectionPort: 443,
            targetScheme: "https",
            targetHost: "other.com",
            targetPort: 443,
            serverCertificate: cert,
            isGoingAway: false);

        Assert.True(decision.CanReuse);
    }
}