using System.Net.Security;
using System.Net.Sockets;
using TurboHttp.Client;
using TurboHttp.IO;

namespace TurboHttp.Tests.IO;

public sealed class TcpOptionsFactoryTests
{
    private static TurboClientOptions DefaultOptions() => new TurboClientOptions();

    // TCP-001: http URI with no explicit port → TcpOptions, Port=80
    [Fact]
    public void TCP_001_Http_DefaultPort_80()
    {
        var result = TcpOptionsFactory.Build(new Uri("http://example.com"), DefaultOptions());

        Assert.IsNotType<TlsOptions>(result);
        Assert.Equal("example.com", result.Host);
        Assert.Equal(80, result.Port);
    }

    // TCP-002: https URI with no explicit port → TlsOptions, Port=443
    [Fact]
    public void TCP_002_Https_DefaultPort_443_ReturnsTlsOptions()
    {
        var result = TcpOptionsFactory.Build(new Uri("https://example.com"), DefaultOptions());

        var tls = Assert.IsType<TlsOptions>(result);
        Assert.Equal("example.com", tls.Host);
        Assert.Equal(443, tls.Port);
    }

    // TCP-003: http URI with explicit port → TcpOptions, Port=8080
    [Fact]
    public void TCP_003_Http_ExplicitPort_8080()
    {
        var result = TcpOptionsFactory.Build(new Uri("http://example.com:8080"), DefaultOptions());

        Assert.IsNotType<TlsOptions>(result);
        Assert.Equal(8080, result.Port);
    }

    // TCP-004: https URI with explicit port → TlsOptions, Port=8443
    [Fact]
    public void TCP_004_Https_ExplicitPort_8443()
    {
        var result = TcpOptionsFactory.Build(new Uri("https://example.com:8443"), DefaultOptions());

        var tls = Assert.IsType<TlsOptions>(result);
        Assert.Equal(8443, tls.Port);
    }

    // TCP-005: IPv4 literal → TcpOptions, AddressFamily=InterNetwork
    [Fact]
    public void TCP_005_IPv4Literal_InterNetwork()
    {
        var result = TcpOptionsFactory.Build(new Uri("http://1.2.3.4"), DefaultOptions());

        Assert.IsNotType<TlsOptions>(result);
        Assert.Equal(AddressFamily.InterNetwork, result.AddressFamily);
    }

    // TCP-006: IPv6 literal → TcpOptions, AddressFamily=InterNetworkV6
    [Fact]
    public void TCP_006_IPv6Literal_InterNetworkV6()
    {
        var result = TcpOptionsFactory.Build(new Uri("http://[::1]"), DefaultOptions());

        Assert.IsNotType<TlsOptions>(result);
        Assert.Equal(AddressFamily.InterNetworkV6, result.AddressFamily);
    }

    // TCP-007: DNS hostname → TcpOptions, AddressFamily=Unspecified
    [Fact]
    public void TCP_007_Hostname_Unspecified()
    {
        var result = TcpOptionsFactory.Build(new Uri("http://hostname"), DefaultOptions());

        Assert.IsNotType<TlsOptions>(result);
        Assert.Equal(AddressFamily.Unspecified, result.AddressFamily);
    }

    // TCP-008: clientOptions.ConnectTimeout=30s → result.ConnectTimeout==30s
    [Fact]
    public void TCP_008_ConnectTimeout_Propagated()
    {
        var opts   = new TurboClientOptions { ConnectTimeout = TimeSpan.FromSeconds(30) };
        var result = TcpOptionsFactory.Build(new Uri("http://example.com"), opts);

        Assert.Equal(TimeSpan.FromSeconds(30), result.ConnectTimeout);
    }

    // TCP-009: clientOptions.ReconnectInterval=2s → result.ReconnectInterval==2s
    [Fact]
    public void TCP_009_ReconnectInterval_Propagated()
    {
        var opts   = new TurboClientOptions { ReconnectInterval = TimeSpan.FromSeconds(2) };
        var result = TcpOptionsFactory.Build(new Uri("http://example.com"), opts);

        Assert.Equal(TimeSpan.FromSeconds(2), result.ReconnectInterval);
    }

    // TCP-010: clientOptions.MaxReconnectAttempts=3 → result.MaxReconnectAttempts==3
    [Fact]
    public void TCP_010_MaxReconnectAttempts_Propagated()
    {
        var opts   = new TurboClientOptions { MaxReconnectAttempts = 3 };
        var result = TcpOptionsFactory.Build(new Uri("http://example.com"), opts);

        Assert.Equal(3, result.MaxReconnectAttempts);
    }

    // TCP-011: clientOptions.MaxFrameSize=256*1024 → result.MaxFrameSize==262144
    [Fact]
    public void TCP_011_MaxFrameSize_Propagated()
    {
        var opts   = new TurboClientOptions { MaxFrameSize = 256 * 1024 };
        var result = TcpOptionsFactory.Build(new Uri("http://example.com"), opts);

        Assert.Equal(256 * 1024, result.MaxFrameSize);
    }

    // TCP-012: https + ServerCertificateValidationCallback set → callback on TlsOptions
    [Fact]
    public void TCP_012_Https_CallbackPropagated()
    {
        RemoteCertificateValidationCallback callback =
            (_, _, _, _) => true;

        var opts   = new TurboClientOptions { ServerCertificateValidationCallback = callback };
        var result = TcpOptionsFactory.Build(new Uri("https://example.com"), opts);

        var tls = Assert.IsType<TlsOptions>(result);
        Assert.Same(callback, tls.ServerCertificateValidationCallback);
    }

    // TCP-013: http + ServerCertificateValidationCallback set → TcpOptions (callback ignored)
    [Fact]
    public void TCP_013_Http_CallbackIgnored_ReturnsTcpOptions()
    {
        RemoteCertificateValidationCallback callback =
            (_, _, _, _) => true;

        var opts   = new TurboClientOptions { ServerCertificateValidationCallback = callback };
        var result = TcpOptionsFactory.Build(new Uri("http://example.com"), opts);

        Assert.IsNotType<TlsOptions>(result);
    }

    // TCP-014: TlsOptions.TargetHost == Host (SNI set automatically)
    [Fact]
    public void TCP_014_TlsOptions_TargetHost_EqualsHost()
    {
        var result = TcpOptionsFactory.Build(new Uri("https://api.example.com"), DefaultOptions());

        var tls = Assert.IsType<TlsOptions>(result);
        Assert.Equal(tls.Host, tls.TargetHost);
    }

    // TCP-015: wss URI → TlsOptions (same as https)
    [Fact]
    public void TCP_015_Wss_ReturnsTlsOptions()
    {
        var result = TcpOptionsFactory.Build(new Uri("wss://example.com"), DefaultOptions());

        Assert.IsType<TlsOptions>(result);
    }
}
