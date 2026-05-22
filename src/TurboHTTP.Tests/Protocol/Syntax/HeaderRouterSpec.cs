using Microsoft.AspNetCore.Http;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax;

namespace TurboHTTP.Tests.Protocol.Syntax;

public sealed class HeaderRouterSpec
{
    [Fact(Timeout = 5000)]
    public void ApplyToHeaderDictionary_should_write_all_headers_flat()
    {
        var parsed = new HeaderCollection();
        parsed.Add("Host", "example.com");
        parsed.Add("Content-Type", "text/plain");
        parsed.Add("Content-Length", "42");
        parsed.Add("Accept", "application/json");

        var dict = new HeaderDictionary();
        HeaderRouter.ApplyToHeaderDictionary(dict, parsed);

        Assert.Equal("example.com", dict["Host"]);
        Assert.Equal("text/plain", dict["Content-Type"]);
        Assert.Equal("42", dict["Content-Length"]);
        Assert.Equal("application/json", dict["Accept"]);
    }

    [Fact(Timeout = 5000)]
    public void ApplyToHeaderDictionary_should_handle_multiple_values()
    {
        var parsed = new HeaderCollection();
        parsed.Add("Accept", "text/html");
        parsed.Add("Accept", "application/json");

        var dict = new HeaderDictionary();
        HeaderRouter.ApplyToHeaderDictionary(dict, parsed);

        var values = dict["Accept"];
        Assert.Equal(2, values.Count);
    }
}
