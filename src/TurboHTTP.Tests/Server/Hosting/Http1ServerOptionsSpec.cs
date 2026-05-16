using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server.Hosting;

public sealed class Http1ServerOptionsSpec
{
    [Fact(Timeout = 5000)]
    public void Http1_should_have_sensible_defaults()
    {
        var options = new TurboServerOptions();

        Assert.Equal(8192, options.Http1.MaxRequestLineLength);
        Assert.Equal(8192, options.Http1.MaxRequestTargetLength);
        Assert.Equal(16, options.Http1.MaxPipelinedRequests);
        Assert.Equal(4096, options.Http1.MaxChunkExtensionLength);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Http1.BodyReadTimeout);
    }
}