using System.Net;
using TurboHTTP.Protocol.Syntax.Http11;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11;

public sealed class Http11ProfileSpec
{
    [Fact(Timeout = 5000)]
    public void SupportsChunked_should_be_true()
    {
        Assert.True(Http11Profile.SupportsChunked);
    }

    [Fact(Timeout = 5000)]
    public void DefaultPersistent_should_be_true()
    {
        Assert.True(Http11Profile.DefaultPersistent);
    }

    [Fact(Timeout = 5000)]
    public void RequiresHost_should_be_true()
    {
        Assert.True(Http11Profile.RequiresHost);
    }

    [Fact(Timeout = 5000)]
    public void Version_should_be_11()
    {
        Assert.Equal(HttpVersion.Version11, Http11Profile.Version);
    }
}