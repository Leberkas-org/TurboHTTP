using System.Net.Http.Headers;
using Microsoft.Extensions.Primitives;
using TurboHTTP.Server.Context.Adapters;

namespace TurboHTTP.Tests.Server.Context;

public sealed class TurboRequestHeaderDictionarySpec
{
    [Fact(Timeout = 5000)]
    public void Indexer_should_return_request_header_value()
    {
        var request = new HttpRequestMessage();
        request.Headers.Add("X-Custom", "value1");
        var dict = new TurboRequestHeaderDictionary(request.Headers, null);

        Assert.Equal("value1", dict["X-Custom"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void Indexer_should_return_content_header_value()
    {
        var content = new StringContent("body");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var dict = new TurboRequestHeaderDictionary(new HttpRequestMessage().Headers, content.Headers);

        Assert.Equal("application/json", dict["Content-Type"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void Indexer_should_return_empty_for_missing_header()
    {
        var dict = new TurboRequestHeaderDictionary(new HttpRequestMessage().Headers, null);
        Assert.Equal(StringValues.Empty, dict["X-Missing"]);
    }

    [Fact(Timeout = 5000)]
    public void ContainsKey_should_find_request_header()
    {
        var request = new HttpRequestMessage();
        request.Headers.Add("Accept", "text/html");
        var dict = new TurboRequestHeaderDictionary(request.Headers, null);

        Assert.True(dict.ContainsKey("Accept"));
    }

    [Fact(Timeout = 5000)]
    public void ContainsKey_should_find_content_header()
    {
        var content = new StringContent("body");
        var dict = new TurboRequestHeaderDictionary(new HttpRequestMessage().Headers, content.Headers);

        Assert.True(dict.ContainsKey("Content-Type"));
    }

    [Fact(Timeout = 5000)]
    public void Count_should_include_both_request_and_content_headers()
    {
        var request = new HttpRequestMessage();
        request.Headers.Add("Accept", "text/html");
        var content = new StringContent("body");
        var dict = new TurboRequestHeaderDictionary(request.Headers, content.Headers);

        Assert.True(dict.Count >= 2);
    }

    [Fact(Timeout = 5000)]
    public void ContentLength_should_read_from_content_headers()
    {
        var content = new ByteArrayContent(new byte[42]);
        var dict = new TurboRequestHeaderDictionary(new HttpRequestMessage().Headers, content.Headers);

        Assert.Equal(42, dict.ContentLength);
    }

    [Fact(Timeout = 5000)]
    public void MultiValue_header_should_return_all_values()
    {
        var request = new HttpRequestMessage();
        request.Headers.Add("Accept", ["text/html", "application/json"]);
        var dict = new TurboRequestHeaderDictionary(request.Headers, null);

        var values = dict["Accept"];
        Assert.Equal(2, values.Count);
    }

    [Fact(Timeout = 5000)]
    public void Set_should_replace_header_value()
    {
        var request = new HttpRequestMessage();
        request.Headers.Add("X-Custom", "old");
        var dict = new TurboRequestHeaderDictionary(request.Headers, null);

        dict["X-Custom"] = "new";

        Assert.Equal("new", dict["X-Custom"].ToString());
    }
}
