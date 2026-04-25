using System.Net;
using Servus.Akka.IO;

namespace Servus.Akka.Tests.IO;

public sealed class RequestEndpointSpec
{
    private static readonly RequestEndpoint TestEndpoint = new()
    {
        Scheme = "https",
        Host = "example.com",
        Port = 443,
        Version = HttpVersion.Version20
    };

    [Fact(Timeout = 5000)]
    public void FromRequest_should_extract_host_port_scheme_version()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com:8443/path")
        {
            Version = HttpVersion.Version20
        };

        var endpoint = RequestEndpoint.FromRequest(request);

        Assert.Equal("example.com", endpoint.Host);
        Assert.Equal((ushort)8443, endpoint.Port);
        Assert.Equal("https", endpoint.Scheme);
        Assert.Equal(HttpVersion.Version20, endpoint.Version);
    }

    [Fact(Timeout = 5000)]
    public void FromRequest_should_use_default_https_port()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path")
        {
            Version = HttpVersion.Version11
        };

        var endpoint = RequestEndpoint.FromRequest(request);

        Assert.Equal((ushort)443, endpoint.Port);
    }

    [Fact(Timeout = 5000)]
    public void FromRequest_should_use_default_http_port()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path")
        {
            Version = HttpVersion.Version11
        };

        var endpoint = RequestEndpoint.FromRequest(request);

        Assert.Equal((ushort)80, endpoint.Port);
    }

    [Fact(Timeout = 5000)]
    public void FromRequest_should_throw_on_null_request()
    {
        Assert.Throws<ArgumentNullException>(() => RequestEndpoint.FromRequest(null!));
    }

    [Fact(Timeout = 5000)]
    public void FromRequest_should_throw_on_null_version()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path")
            {
                Version = null!
            };
        });
    }

    [Fact(Timeout = 5000)]
    public void FromRequest_should_throw_on_null_request_uri()
    {
        var request = new HttpRequestMessage
        {
            Version = HttpVersion.Version11,
            RequestUri = null
        };

        Assert.Throws<ArgumentNullException>(() => RequestEndpoint.FromRequest(request));
    }

    [Fact(Timeout = 5000)]
    public void Default_should_return_empty_endpoint()
    {
        var def = RequestEndpoint.Default;

        Assert.Equal(string.Empty, def.Host);
        Assert.Equal(string.Empty, def.Scheme);
        Assert.Equal(ushort.MinValue, def.Port);
        Assert.Equal(HttpVersion.Unknown, def.Version);
    }

    [Fact(Timeout = 5000)]
    public void Equals_should_be_case_insensitive_for_host()
    {
        var upper = TestEndpoint with { Host = "EXAMPLE.COM" };
        var lower = TestEndpoint with { Host = "example.com" };

        Assert.Equal(upper, lower);
    }

    [Fact(Timeout = 5000)]
    public void Equals_should_be_case_insensitive_for_scheme()
    {
        var upper = TestEndpoint with { Scheme = "HTTPS" };
        var lower = TestEndpoint with { Scheme = "https" };

        Assert.Equal(upper, lower);
    }

    [Fact(Timeout = 5000)]
    public void Equals_should_be_sensitive_for_port()
    {
        var port443 = TestEndpoint with { Port = 443 };
        var port8443 = TestEndpoint with { Port = 8443 };

        Assert.NotEqual(port443, port8443);
    }

    [Fact(Timeout = 5000)]
    public void Equals_should_be_sensitive_for_version()
    {
        var http20 = TestEndpoint with { Version = HttpVersion.Version20 };
        var http11 = TestEndpoint with { Version = HttpVersion.Version11 };

        Assert.NotEqual(http20, http11);
    }

    [Fact(Timeout = 5000)]
    public void Equals_should_match_identical_endpoints()
    {
        var a = TestEndpoint;
        var b = TestEndpoint;

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact(Timeout = 5000)]
    public void Equals_should_not_match_default_and_populated()
    {
        Assert.NotEqual(RequestEndpoint.Default, TestEndpoint);
    }

    [Fact(Timeout = 5000)]
    public void GetHashCode_should_be_consistent()
    {
        var hash1 = TestEndpoint.GetHashCode();
        var hash2 = TestEndpoint.GetHashCode();

        Assert.Equal(hash1, hash2);
    }

    [Fact(Timeout = 5000)]
    public void GetHashCode_should_be_case_insensitive_for_host()
    {
        var upper = TestEndpoint with { Host = "EXAMPLE.COM" };
        var lower = TestEndpoint with { Host = "example.com" };

        Assert.Equal(upper.GetHashCode(), lower.GetHashCode());
    }

    [Fact(Timeout = 5000)]
    public void GetHashCode_should_be_case_insensitive_for_scheme()
    {
        var upper = TestEndpoint with { Scheme = "HTTPS" };
        var lower = TestEndpoint with { Scheme = "https" };

        Assert.Equal(upper.GetHashCode(), lower.GetHashCode());
    }

    [Fact(Timeout = 5000)]
    public void GetHashCode_should_differ_for_different_ports()
    {
        var port443 = TestEndpoint with { Port = 443 };
        var port8443 = TestEndpoint with { Port = 8443 };

        Assert.NotEqual(port443.GetHashCode(), port8443.GetHashCode());
    }

    [Fact(Timeout = 5000)]
    public void Inequality_operator_should_detect_different_endpoints()
    {
        var a = TestEndpoint;
        var b = TestEndpoint with { Port = 8080 };

        Assert.True(a != b);
        Assert.False(a == b);
    }
}
