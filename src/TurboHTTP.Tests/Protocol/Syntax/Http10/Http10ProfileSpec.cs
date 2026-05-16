using System.Net;
using TurboHTTP.Protocol.Syntax.Http10;

namespace TurboHTTP.Tests.Protocol.Syntax.Http10;

public sealed class Http10ProfileSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void Profile_should_expose_RFC1945_defaults()
    {
        var p = Http10Profile.Default;
        Assert.False(p.SupportsChunked);
        Assert.False(p.DefaultPersistent);
        Assert.False(p.RequiresHost);
        Assert.Equal(HttpVersion.Version10, p.Version);
    }
}