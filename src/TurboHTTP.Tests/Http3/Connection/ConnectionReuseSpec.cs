using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.Tests.Http3.Connection;

public sealed class ConnectionReuseSpec : IDisposable
{
    private readonly X509Certificate2 _singleHostCert = CreateSelfSignedCert("example.com");
    private readonly X509Certificate2 _wildcardCert = CreateSelfSignedCert("*.example.com");

    private readonly X509Certificate2 _multiSanCert =
        CreateSelfSignedCert("alpha.example.com", "beta.example.com", "gamma.example.com");

    private readonly X509Certificate2 _cnOnlyCert = CreateSelfSignedCertCnOnly("cn-only.example.com");

    public void Dispose()
    {
        _singleHostCert.Dispose();
        _wildcardCert.Dispose();
        _multiSanCert.Dispose();
        _cnOnlyCert.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    public void Should_AllowReuse_When_SameOrigin()
    {
        var decision = ConnectionReuseEvaluator.Evaluate(
            "https", "example.com", 443,
            "https", "example.com", 443,
            _singleHostCert);

        Assert.True(decision.CanReuse);
        Assert.Contains("Same origin", decision.Reason);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    public void Should_AllowReuse_When_SameOriginDifferentCase()
    {
        var decision = ConnectionReuseEvaluator.Evaluate(
            "HTTPS", "Example.COM", 443,
            "https", "example.com", 443,
            _singleHostCert);

        Assert.True(decision.CanReuse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    public void Should_RequireNewConnection_When_DifferentPort()
    {
        var decision = ConnectionReuseEvaluator.Evaluate(
            "https", "example.com", 443,
            "https", "example.com", 8443,
            _singleHostCert);

        Assert.False(decision.CanReuse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    public void Should_RequireNewConnection_When_DifferentScheme()
    {
        var decision = ConnectionReuseEvaluator.Evaluate(
            "https", "example.com", 443,
            "http", "example.com", 443,
            _singleHostCert);

        Assert.False(decision.CanReuse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    public void Should_AllowReuse_When_CertCoversTargetHost()
    {
        var decision = ConnectionReuseEvaluator.Evaluate(
            "https", "alpha.example.com", 443,
            "https", "beta.example.com", 443,
            _multiSanCert);

        Assert.True(decision.CanReuse);
        Assert.Contains("cross-origin", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    public void Should_RequireNewConnection_When_CertDoesNotCoverTarget()
    {
        var decision = ConnectionReuseEvaluator.Evaluate(
            "https", "example.com", 443,
            "https", "other.com", 443,
            _singleHostCert);

        Assert.False(decision.CanReuse);
        Assert.Contains("does not cover", decision.Reason);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    public void Should_AllowReuse_When_WildcardCertCoversTarget()
    {
        var decision = ConnectionReuseEvaluator.Evaluate(
            "https", "foo.example.com", 443,
            "https", "bar.example.com", 443,
            _wildcardCert);

        Assert.True(decision.CanReuse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    public void Should_RequireNewConnection_When_WildcardDoesNotCoverSubSubdomain()
    {
        var decision = ConnectionReuseEvaluator.Evaluate(
            "https", "foo.example.com", 443,
            "https", "a.b.example.com", 443,
            _wildcardCert);

        Assert.False(decision.CanReuse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    public void Should_RequireNewConnection_When_NoCertificate()
    {
        var decision = ConnectionReuseEvaluator.Evaluate(
            "https", "example.com", 443,
            "https", "other.example.com", 443,
            serverCertificate: null);

        Assert.False(decision.CanReuse);
        Assert.Contains("No server certificate", decision.Reason);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void Should_RequireNewConnection_When_GoAwayReceived()
    {
        var decision = ConnectionReuseEvaluator.Evaluate(
            "https", "example.com", 443,
            "https", "example.com", 443,
            _singleHostCert,
            isGoingAway: true);

        Assert.False(decision.CanReuse);
        Assert.Contains("GOAWAY", decision.Reason);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    [InlineData("example.com", true)]
    [InlineData("EXAMPLE.COM", true)]
    [InlineData("other.com", false)]
    public void Should_MatchExactSan(string hostname, bool expected)
    {
        Assert.Equal(expected, ConnectionReuseEvaluator.CoversHostname(_singleHostCert, hostname));
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    [InlineData("foo.example.com", true)]
    [InlineData("bar.example.com", true)]
    [InlineData("example.com", false)]
    [InlineData("sub.foo.example.com", false)]
    public void Should_MatchWildcardSan(string hostname, bool expected)
    {
        Assert.Equal(expected, ConnectionReuseEvaluator.CoversHostname(_wildcardCert, hostname));
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    [InlineData("alpha.example.com", true)]
    [InlineData("beta.example.com", true)]
    [InlineData("gamma.example.com", true)]
    [InlineData("delta.example.com", false)]
    public void Should_MatchMultipleSans(string hostname, bool expected)
    {
        Assert.Equal(expected, ConnectionReuseEvaluator.CoversHostname(_multiSanCert, hostname));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    public void Should_FallbackToCn_When_NoSanExists()
    {
        Assert.True(ConnectionReuseEvaluator.CoversHostname(_cnOnlyCert, "cn-only.example.com"));
        Assert.False(ConnectionReuseEvaluator.CoversHostname(_cnOnlyCert, "other.example.com"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    public void Should_Throw_When_CertIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ConnectionReuseEvaluator.CoversHostname(null!, "example.com"));
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    [InlineData("")]
    [InlineData("  ")]
    public void Should_Throw_When_HostnameIsEmpty(string hostname)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            ConnectionReuseEvaluator.CoversHostname(_singleHostCert, hostname));
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.3")]
    [InlineData("*.com", "example.com", true)]
    [InlineData("*.example.com", "example.com", false)]
    [InlineData("*.example.com", ".example.com", false)]
    [InlineData("example.com", "example.com", true)]
    [InlineData("EXAMPLE.COM", "example.com", true)]
    public void Should_HandleMatchEdgeCases(string certName, string hostname, bool expected)
    {
        Assert.Equal(expected, ConnectionReuseEvaluator.MatchesHostname(certName, hostname));
    }

    private static X509Certificate2 CreateSelfSignedCert(params string[] sanNames)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={sanNames[0]}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        foreach (var name in sanNames)
        {
            sanBuilder.AddDnsName(name);
        }

        request.CertificateExtensions.Add(sanBuilder.Build());

        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));
        return cert;
    }

    private static X509Certificate2 CreateSelfSignedCertCnOnly(string commonName)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={commonName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Deliberately do NOT add a SAN extension — forces CN fallback.
        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));
        return cert;
    }
}