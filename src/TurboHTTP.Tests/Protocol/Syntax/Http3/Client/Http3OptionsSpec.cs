using TurboHTTP.Client;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client;

public sealed class Http3OptionsSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4.1")]
    public void Http3Options_should_have_correct_defaults()
    {
        var options = new Http3Options();

        Assert.Equal(4, options.MaxConnectionsPerServer);
        Assert.Equal(16_384, options.QpackMaxTableCapacity);
        Assert.Equal(100, options.QpackBlockedStreams);
        Assert.Equal(65536, options.MaxFieldSectionSize);
        Assert.Equal(TimeSpan.FromSeconds(30), options.IdleTimeout);
        Assert.Equal(3, options.MaxReconnectAttempts);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4.1")]
    public void Http3Options_should_allow_custom_values()
    {
        var options = new Http3Options
        {
            MaxConnectionsPerServer = 8,
            QpackMaxTableCapacity = 8192,
            QpackBlockedStreams = 200,
            MaxFieldSectionSize = 131072,
            IdleTimeout = TimeSpan.FromSeconds(60),
            MaxReconnectAttempts = 5,
        };

        Assert.Equal(8, options.MaxConnectionsPerServer);
        Assert.Equal(8192, options.QpackMaxTableCapacity);
        Assert.Equal(200, options.QpackBlockedStreams);
        Assert.Equal(131072, options.MaxFieldSectionSize);
        Assert.Equal(TimeSpan.FromSeconds(60), options.IdleTimeout);
        Assert.Equal(5, options.MaxReconnectAttempts);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4.1")]
    public void TurboClientOptions_should_expose_Http3Options_with_defaults()
    {
        var clientOptions = new TurboClientOptions();

        Assert.NotNull(clientOptions.Http3);
        Assert.Equal(4, clientOptions.Http3.MaxConnectionsPerServer);
        Assert.Equal(16_384, clientOptions.Http3.QpackMaxTableCapacity);
    }
}