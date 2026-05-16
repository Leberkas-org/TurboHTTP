using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Protocol.Semantics;

public sealed class HeaderCollectionSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.3")]
    public void HeaderCollection_should_preserve_insertion_order()
    {
        var headers = new HeaderCollection
        {
            { "Host", "example.com" },
            { "User-Agent", "test/1.0" },
            { "Accept", "*/*" }
        };

        var names = headers.Select(h => h.Name).ToArray();
        Assert.Equal(new[] { "Host", "User-Agent", "Accept" }, names);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.3")]
    public void HeaderCollection_should_allow_multiple_values_for_same_name()
    {
        var headers = new HeaderCollection
        {
            { "Set-Cookie", "a=1" },
            { "Set-Cookie", "b=2" }
        };

        Assert.Equal(new[] { "a=1", "b=2" }, headers.GetValues("Set-Cookie").ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.3")]
    public void HeaderCollection_should_combine_values_with_comma_when_joining()
    {
        var headers = new HeaderCollection
        {
            { "Accept", "text/html" },
            { "Accept", "application/json" }
        };

        Assert.Equal("text/html, application/json", headers.GetCombined("Accept"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.3")]
    public void HeaderCollection_should_lookup_case_insensitive()
    {
        var headers = new HeaderCollection { { "content-type", "text/html" } };

        Assert.Equal(new[] { "text/html" }, headers.GetValues("Content-Type").ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.3")]
    public void HeaderCollection_should_return_null_GetCombined_when_missing()
    {
        var headers = new HeaderCollection();
        Assert.Null(headers.GetCombined("Host"));
        Assert.Empty(headers.GetValues("Host"));
    }

    [Fact(Timeout = 5000)]
    public void HeaderCollection_should_clear_all_entries()
    {
        var headers = new HeaderCollection
        {
            { "A", "1" },
            { "B", "2" }
        };
        headers.Clear();
        Assert.Equal(0, headers.Count);
    }
}