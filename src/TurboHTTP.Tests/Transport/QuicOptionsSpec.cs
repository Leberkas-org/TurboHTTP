using System.Net.Security;
using TurboHTTP.Transport.Connection;

#pragma warning disable CA1416

namespace TurboHTTP.Tests.Transport;

#pragma warning disable CA1416

public sealed class QuicOptionsSpec
{
    [Fact(Timeout = 5000)]
    public void QuicOptions_should_inherit_host_from_tls_options()
    {
        var options = new QuicOptions { Host = "example.com", Port = 443 };

        Assert.Equal("example.com", options.Host);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_inherit_port_from_tls_options()
    {
        var options = new QuicOptions { Host = "example.com", Port = 8443 };

        Assert.Equal(8443, options.Port);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_inherit_target_host_from_tls_options()
    {
        var options = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            TargetHost = "sni.example.com"
        };

        Assert.Equal("sni.example.com", options.TargetHost);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_inherit_application_protocols_from_tls_options()
    {
        var protocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http3 };
        var options = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            ApplicationProtocols = protocols
        };

        Assert.Same(protocols, options.ApplicationProtocols);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_default_idle_timeout_to_30_seconds()
    {
        var options = new QuicOptions { Host = "example.com", Port = 443 };

        Assert.Equal(TimeSpan.FromSeconds(30), options.IdleTimeout);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_allow_custom_idle_timeout()
    {
        var timeout = TimeSpan.FromSeconds(60);
        var options = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            IdleTimeout = timeout
        };

        Assert.Equal(timeout, options.IdleTimeout);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_allow_very_short_idle_timeout()
    {
        var timeout = TimeSpan.FromMilliseconds(1);
        var options = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            IdleTimeout = timeout
        };

        Assert.Equal(timeout, options.IdleTimeout);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_allow_very_long_idle_timeout()
    {
        var timeout = TimeSpan.FromMinutes(30);
        var options = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            IdleTimeout = timeout
        };

        Assert.Equal(timeout, options.IdleTimeout);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_default_max_bidirectional_streams_to_100()
    {
        var options = new QuicOptions { Host = "example.com", Port = 443 };

        Assert.Equal(100, options.MaxBidirectionalStreams);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_allow_custom_max_bidirectional_streams()
    {
        var options = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            MaxBidirectionalStreams = 200
        };

        Assert.Equal(200, options.MaxBidirectionalStreams);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_allow_zero_max_bidirectional_streams()
    {
        var options = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            MaxBidirectionalStreams = 0
        };

        Assert.Equal(0, options.MaxBidirectionalStreams);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_default_max_unidirectional_streams_to_3()
    {
        var options = new QuicOptions { Host = "example.com", Port = 443 };

        Assert.Equal(3, options.MaxUnidirectionalStreams);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_allow_custom_max_unidirectional_streams()
    {
        var options = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            MaxUnidirectionalStreams = 10
        };

        Assert.Equal(10, options.MaxUnidirectionalStreams);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_allow_zero_max_unidirectional_streams()
    {
        var options = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            MaxUnidirectionalStreams = 0
        };

        Assert.Equal(0, options.MaxUnidirectionalStreams);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_default_allow_early_data_to_false()
    {
        var options = new QuicOptions { Host = "example.com", Port = 443 };

        Assert.False(options.AllowEarlyData);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_allow_early_data_true()
    {
        var options = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            AllowEarlyData = true
        };

        Assert.True(options.AllowEarlyData);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_default_allow_connection_migration_to_true()
    {
        var options = new QuicOptions { Host = "example.com", Port = 443 };

        Assert.True(options.AllowConnectionMigration);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_allow_disable_connection_migration()
    {
        var options = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            AllowConnectionMigration = false
        };

        Assert.False(options.AllowConnectionMigration);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_be_equal_with_same_values()
    {
        var options1 = new QuicOptions { Host = "example.com", Port = 443 };
        var options2 = new QuicOptions { Host = "example.com", Port = 443 };

        Assert.Equal(options1, options2);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_not_be_equal_with_different_idle_timeout()
    {
        var options1 = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            IdleTimeout = TimeSpan.FromSeconds(30)
        };

        var options2 = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            IdleTimeout = TimeSpan.FromSeconds(60)
        };

        Assert.NotEqual(options1, options2);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_not_be_equal_with_different_max_bidirectional_streams()
    {
        var options1 = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            MaxBidirectionalStreams = 100
        };

        var options2 = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            MaxBidirectionalStreams = 200
        };

        Assert.NotEqual(options1, options2);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_not_be_equal_with_different_max_unidirectional_streams()
    {
        var options1 = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            MaxUnidirectionalStreams = 3
        };

        var options2 = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            MaxUnidirectionalStreams = 10
        };

        Assert.NotEqual(options1, options2);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_not_be_equal_with_different_allow_early_data()
    {
        var options1 = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            AllowEarlyData = true
        };

        var options2 = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            AllowEarlyData = false
        };

        Assert.NotEqual(options1, options2);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_not_be_equal_with_different_allow_connection_migration()
    {
        var options1 = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            AllowConnectionMigration = true
        };

        var options2 = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            AllowConnectionMigration = false
        };

        Assert.NotEqual(options1, options2);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_support_very_large_stream_limits()
    {
        var options = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            MaxBidirectionalStreams = int.MaxValue,
            MaxUnidirectionalStreams = int.MaxValue
        };

        Assert.Equal(int.MaxValue, options.MaxBidirectionalStreams);
        Assert.Equal(int.MaxValue, options.MaxUnidirectionalStreams);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_support_zero_timeout()
    {
        var options = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            IdleTimeout = TimeSpan.Zero
        };

        Assert.Equal(TimeSpan.Zero, options.IdleTimeout);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_allow_combining_early_data_and_connection_migration()
    {
        var options = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            AllowEarlyData = true,
            AllowConnectionMigration = true
        };

        Assert.True(options.AllowEarlyData);
        Assert.True(options.AllowConnectionMigration);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_allow_disabling_both_early_data_and_connection_migration()
    {
        var options = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            AllowEarlyData = false,
            AllowConnectionMigration = false
        };

        Assert.False(options.AllowEarlyData);
        Assert.False(options.AllowConnectionMigration);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_hash_code_should_be_consistent()
    {
        var options = new QuicOptions { Host = "example.com", Port = 443 };

        var hash1 = options.GetHashCode();
        var hash2 = options.GetHashCode();

        Assert.Equal(hash1, hash2);
    }

    [Fact(Timeout = 5000)]
    public void QuicOptions_should_support_full_configuration()
    {
        var protocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http3 };
        var options = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            TargetHost = "sni.example.com",
            ApplicationProtocols = protocols,
            IdleTimeout = TimeSpan.FromMinutes(5),
            MaxBidirectionalStreams = 250,
            MaxUnidirectionalStreams = 25,
            AllowEarlyData = true,
            AllowConnectionMigration = true,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        };

        Assert.Equal("example.com", options.Host);
        Assert.Equal(443, options.Port);
        Assert.Equal("sni.example.com", options.TargetHost);
        Assert.NotNull(options.ApplicationProtocols);
        Assert.Equal(TimeSpan.FromMinutes(5), options.IdleTimeout);
        Assert.Equal(250, options.MaxBidirectionalStreams);
        Assert.Equal(25, options.MaxUnidirectionalStreams);
        Assert.True(options.AllowEarlyData);
        Assert.True(options.AllowConnectionMigration);
        Assert.Equal(TimeSpan.FromSeconds(15), options.ConnectTimeout);
    }
}