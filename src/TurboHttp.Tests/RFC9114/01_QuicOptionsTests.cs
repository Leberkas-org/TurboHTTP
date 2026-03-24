using System.Net.Security;
using TurboHttp.Transport;

namespace TurboHttp.Tests.RFC9114;

public sealed class QuicOptionsTests
{
    [Fact(DisplayName = "RFC9114-3.2-QO-001: QuicOptions inherits TcpOptions")]
    public void Should_InheritTcpOptions_When_Created()
    {
        var options = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            MaxFrameSize = 64 * 1024,
        };

        Assert.Equal("example.com", options.Host);
        Assert.Equal(443, options.Port);
        Assert.Equal(TimeSpan.FromSeconds(5), options.ConnectTimeout);
        Assert.Equal(64 * 1024, options.MaxFrameSize);
        Assert.IsAssignableFrom<TcpOptions>(options);
    }

    [Fact(DisplayName = "RFC9114-3.2-QO-002: Default ALPN is h3")]
    public void Should_HaveH3Alpn_When_Default()
    {
        var options = new QuicOptions { Host = "localhost", Port = 443 };

        Assert.Single(options.ApplicationProtocols);
        Assert.Equal(new SslApplicationProtocol("h3"), options.ApplicationProtocols[0]);
    }

    [Fact(DisplayName = "RFC9114-3.2-QO-003: IdleTimeout defaults to 30s")]
    public void Should_Have30sIdle_When_Default()
    {
        var options = new QuicOptions { Host = "localhost", Port = 443 };

        Assert.Equal(TimeSpan.FromSeconds(30), options.IdleTimeout);
    }

    [Fact(DisplayName = "RFC9114-3.2-QO-004: MaxBidirectionalStreams defaults to 100")]
    public void Should_Have100BidiStreams_When_Default()
    {
        var options = new QuicOptions { Host = "localhost", Port = 443 };

        Assert.Equal(100, options.MaxBidirectionalStreams);
    }

    [Fact(DisplayName = "RFC9114-3.2-QO-005: MaxUnidirectionalStreams defaults to 3")]
    public void Should_Have3UniStreams_When_Default()
    {
        var options = new QuicOptions { Host = "localhost", Port = 443 };

        Assert.Equal(3, options.MaxUnidirectionalStreams);
    }
}
