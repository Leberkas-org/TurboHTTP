using TurboHTTP.Server.Context.Adapters;

namespace TurboHTTP.Tests.Server.Context.Adapters;

public sealed class TurboQueryCollectionSpec
{
    [Fact(Timeout = 5000)]
    public void Query_should_parse_single_parameter()
    {
        var query = new TurboQueryCollection("?name=Alice");
        Assert.Equal("Alice", query["name"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void Query_should_parse_multiple_parameters()
    {
        var query = new TurboQueryCollection("?name=Alice&age=30");
        Assert.Equal("Alice", query["name"].ToString());
        Assert.Equal("30", query["age"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void Query_should_return_empty_for_missing_key()
    {
        var query = new TurboQueryCollection("?name=Alice");
        Assert.Equal(Microsoft.Extensions.Primitives.StringValues.Empty, query["missing"]);
    }

    [Fact(Timeout = 5000)]
    public void Query_should_handle_empty_query_string()
    {
        var query = new TurboQueryCollection("");
        Assert.Equal(0, query.Count);
    }

    [Fact(Timeout = 5000)]
    public void Query_should_handle_null_query_string()
    {
        var query = new TurboQueryCollection(null);
        Assert.Equal(0, query.Count);
    }

    [Fact(Timeout = 5000)]
    public void Query_should_decode_url_encoded_values()
    {
        var query = new TurboQueryCollection("?message=hello%20world");
        Assert.Equal("hello world", query["message"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void Query_should_support_duplicate_keys()
    {
        var query = new TurboQueryCollection("?tag=a&tag=b");
        var values = query["tag"];
        Assert.Equal(2, values.Count);
    }
}
