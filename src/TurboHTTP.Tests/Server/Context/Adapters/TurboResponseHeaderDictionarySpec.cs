using Microsoft.Extensions.Primitives;
using TurboHTTP.Server.Context.Adapters;

namespace TurboHTTP.Tests.Server.Context.Adapters;

public sealed class TurboResponseHeaderDictionarySpec
{
    [Fact(Timeout = 5000)]
    public void Indexer_should_return_stored_value()
    {
        var dict = new TurboResponseHeaderDictionary();
        dict["X-Custom"] = "value1";
        Assert.Equal("value1", dict["X-Custom"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void Indexer_should_return_empty_for_missing_header()
    {
        var dict = new TurboResponseHeaderDictionary();
        Assert.Equal(StringValues.Empty, dict["X-Missing"]);
    }

    [Fact(Timeout = 5000)]
    public void Set_should_replace_header_value()
    {
        var dict = new TurboResponseHeaderDictionary();
        dict["X-Custom"] = "old";
        dict["X-Custom"] = "new";
        Assert.Equal("new", dict["X-Custom"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void ContentLength_should_read_from_content_length_header()
    {
        var dict = new TurboResponseHeaderDictionary();
        dict["Content-Length"] = "100";
        Assert.Equal(100, dict.ContentLength);
    }

    [Fact(Timeout = 5000)]
    public void ContentLength_set_should_update_header()
    {
        var dict = new TurboResponseHeaderDictionary();
        dict.ContentLength = 200;
        Assert.Equal("200", dict["Content-Length"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void Count_should_reflect_stored_headers()
    {
        var dict = new TurboResponseHeaderDictionary();
        dict["X-A"] = "1";
        dict["X-B"] = "2";
        Assert.Equal(2, dict.Count);
    }

    [Fact(Timeout = 5000)]
    public void Remove_should_delete_header()
    {
        var dict = new TurboResponseHeaderDictionary();
        dict["X-Custom"] = "value";
        Assert.True(dict.Remove("X-Custom"));
        Assert.Equal(StringValues.Empty, dict["X-Custom"]);
    }

    [Fact(Timeout = 5000)]
    public void ContainsKey_should_find_existing_header()
    {
        var dict = new TurboResponseHeaderDictionary();
        dict["X-Custom"] = "value";
        Assert.True(dict.ContainsKey("X-Custom"));
        Assert.False(dict.ContainsKey("X-Missing"));
    }

    [Fact(Timeout = 5000)]
    public void Clear_should_remove_all_headers()
    {
        var dict = new TurboResponseHeaderDictionary();
        dict["X-A"] = "1";
        dict["X-B"] = "2";
        dict.Clear();
        Assert.Equal(0, dict.Count);
    }

    [Fact(Timeout = 5000)]
    public void Keys_should_be_case_insensitive()
    {
        var dict = new TurboResponseHeaderDictionary();
        dict["Content-Type"] = "text/html";
        Assert.Equal("text/html", dict["content-type"].ToString());
    }
}
