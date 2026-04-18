using System.Net;
using TurboHTTP.Internal;

namespace TurboHTTP.Tests.Internal;

public sealed class RequestEndpointSpec
{
    [Fact(Timeout = 5000)]
    public void RequestEndpoint_should_set_properties_from_initializer()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        Assert.Equal("https", endpoint.Scheme);
        Assert.Equal("example.com", endpoint.Host);
        Assert.Equal((ushort)443, endpoint.Port);
        Assert.Equal(HttpVersion.Version20, endpoint.Version);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_should_support_http_version_10()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "http",
            Host = "localhost",
            Port = 80,
            Version = HttpVersion.Version10
        };

        Assert.Equal(HttpVersion.Version10, endpoint.Version);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_should_support_http_version_11()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "http",
            Host = "localhost",
            Port = 80,
            Version = HttpVersion.Version11
        };

        Assert.Equal(HttpVersion.Version11, endpoint.Version);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_should_support_http_version_20()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "https",
            Host = "localhost",
            Port = 443,
            Version = HttpVersion.Version20
        };

        Assert.Equal(HttpVersion.Version20, endpoint.Version);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_should_support_http_version_30()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "https",
            Host = "localhost",
            Port = 443,
            Version = new Version(3, 0)
        };

        Assert.Equal(new Version(3, 0), endpoint.Version);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_should_be_equal_with_same_values()
    {
        var endpoint1 = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        var endpoint2 = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        Assert.Equal(endpoint1, endpoint2);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_should_be_not_equal_with_different_host()
    {
        var endpoint1 = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        var endpoint2 = new RequestEndpoint
        {
            Scheme = "https",
            Host = "different.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        Assert.NotEqual(endpoint1, endpoint2);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_should_be_not_equal_with_different_port()
    {
        var endpoint1 = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        var endpoint2 = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 8443,
            Version = HttpVersion.Version20
        };

        Assert.NotEqual(endpoint1, endpoint2);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_should_be_not_equal_with_different_version()
    {
        var endpoint1 = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        var endpoint2 = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version11
        };

        Assert.NotEqual(endpoint1, endpoint2);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_should_be_not_equal_with_different_scheme()
    {
        var endpoint1 = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        var endpoint2 = new RequestEndpoint
        {
            Scheme = "http",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        Assert.NotEqual(endpoint1, endpoint2);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_should_have_equal_hash_codes_for_equal_endpoints()
    {
        var endpoint1 = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        var endpoint2 = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        Assert.Equal(endpoint1.GetHashCode(), endpoint2.GetHashCode());
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_should_have_different_hash_codes_for_different_ports()
    {
        var endpoint1 = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        var endpoint2 = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 8443,
            Version = HttpVersion.Version20
        };

        Assert.NotEqual(endpoint1.GetHashCode(), endpoint2.GetHashCode());
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_should_compare_host_case_insensitively()
    {
        var endpoint1 = new RequestEndpoint
        {
            Scheme = "https",
            Host = "EXAMPLE.COM",
            Port = 443,
            Version = HttpVersion.Version20
        };

        var endpoint2 = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        Assert.Equal(endpoint1, endpoint2);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_should_compare_scheme_case_insensitively()
    {
        var endpoint1 = new RequestEndpoint
        {
            Scheme = "HTTPS",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        var endpoint2 = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        Assert.Equal(endpoint1, endpoint2);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_FromRequest_should_extract_endpoint_from_request_message()
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
    public void RequestEndpoint_FromRequest_should_use_default_https_port()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path")
        {
            Version = HttpVersion.Version20
        };

        var endpoint = RequestEndpoint.FromRequest(request);

        Assert.Equal((ushort)443, endpoint.Port);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_FromRequest_should_use_default_http_port()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path")
        {
            Version = HttpVersion.Version11
        };

        var endpoint = RequestEndpoint.FromRequest(request);

        Assert.Equal((ushort)80, endpoint.Port);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_FromRequest_should_throw_on_null_request()
    {
        Assert.Throws<ArgumentNullException>(() => RequestEndpoint.FromRequest(null!));
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_FromRequest_should_throw_on_null_uri()
    {
        var request = new HttpRequestMessage();
        Assert.Throws<ArgumentNullException>(() => RequestEndpoint.FromRequest(request));
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_FromRequest_should_not_accept_null_request()
    {
        // The Version check in FromRequest is defensive; HttpRequestMessage itself prevents null Version assignment.
        // We test the null request validation which is the primary guard.
        Assert.Throws<ArgumentNullException>(() => RequestEndpoint.FromRequest(null!));
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_Default_should_have_empty_values()
    {
        var endpoint = RequestEndpoint.Default;

        Assert.Empty(endpoint.Scheme);
        Assert.Empty(endpoint.Host);
        Assert.Equal(ushort.MinValue, endpoint.Port);
        Assert.Equal(HttpVersion.Unknown, endpoint.Version);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_hash_code_should_be_consistent_across_calls()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        var hash1 = endpoint.GetHashCode();
        var hash2 = endpoint.GetHashCode();

        Assert.Equal(hash1, hash2);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_should_support_wss_scheme()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "wss",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version11
        };

        Assert.Equal("wss", endpoint.Scheme);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_should_support_custom_ports()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 9999,
            Version = HttpVersion.Version20
        };

        Assert.Equal((ushort)9999, endpoint.Port);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_should_support_localhost()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "http",
            Host = "localhost",
            Port = 8080,
            Version = HttpVersion.Version11
        };

        Assert.Equal("localhost", endpoint.Host);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_should_support_ip_addresses()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "https",
            Host = "192.168.1.1",
            Port = 443,
            Version = HttpVersion.Version20
        };

        Assert.Equal("192.168.1.1", endpoint.Host);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_should_handle_subdomain_hosts()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "https",
            Host = "api.v2.example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        Assert.Equal("api.v2.example.com", endpoint.Host);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_equality_should_be_reflexive()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        Assert.Equal(endpoint, endpoint);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_equality_should_be_symmetric()
    {
        var endpoint1 = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        var endpoint2 = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        Assert.Equal(endpoint1, endpoint2);
        Assert.Equal(endpoint2, endpoint1);
    }

    [Fact(Timeout = 5000)]
    public void RequestEndpoint_equality_should_be_transitive()
    {
        var endpoint1 = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        var endpoint2 = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        var endpoint3 = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        Assert.Equal(endpoint1, endpoint2);
        Assert.Equal(endpoint2, endpoint3);
        Assert.Equal(endpoint1, endpoint3);
    }
}