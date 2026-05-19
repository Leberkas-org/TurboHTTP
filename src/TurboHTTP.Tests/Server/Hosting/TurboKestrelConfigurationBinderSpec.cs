using System.Security.Authentication;
using Microsoft.Extensions.Configuration;
using TurboHTTP.Server;
using TurboHTTP.Server.Hosting;

namespace TurboHTTP.Tests.Server.Hosting;

public sealed class TurboKestrelConfigurationBinderSpec
{
    [Fact(Timeout = 5000)]
    public void Bind_should_parse_http_endpoint()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["TurboKestrel:Endpoints:Http:Url"] = "http://localhost:5000"
        });

        var options = new TurboServerOptions();
        TurboKestrelConfigurationBinder.Bind(options, config.GetSection("TurboKestrel"));

        Assert.Contains("http://localhost:5000", options.Urls);
    }

    [Fact(Timeout = 5000)]
    public void Bind_should_parse_https_endpoint_with_certificate()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["TurboKestrel:Endpoints:Https:Url"] = "https://localhost:5001",
            ["TurboKestrel:Endpoints:Https:Certificate:Path"] = "certs/server.pfx",
            ["TurboKestrel:Endpoints:Https:Certificate:Password"] = "changeit"
        });

        var options = new TurboServerOptions();
        TurboKestrelConfigurationBinder.Bind(options, config.GetSection("TurboKestrel"));

        Assert.Single(options.ListenOptions);
        Assert.True(options.ListenOptions[0].IsHttps);
        Assert.Equal("certs/server.pfx", options.ListenOptions[0].HttpsOptions!.CertificatePath);
        Assert.Equal("changeit", options.ListenOptions[0].HttpsOptions!.CertificatePassword);
    }

    [Fact(Timeout = 5000)]
    public void Bind_should_parse_ssl_protocols()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["TurboKestrel:Endpoints:Https:Url"] = "https://localhost:5001",
            ["TurboKestrel:Endpoints:Https:Certificate:Path"] = "cert.pfx",
            ["TurboKestrel:Endpoints:Https:SslProtocols"] = "Tls12, Tls13"
        });

        var options = new TurboServerOptions();
        TurboKestrelConfigurationBinder.Bind(options, config.GetSection("TurboKestrel"));

        Assert.Equal(SslProtocols.Tls12 | SslProtocols.Tls13, options.ListenOptions[0].HttpsOptions!.EnabledSslProtocols);
    }

    [Fact(Timeout = 5000)]
    public void Bind_should_parse_http_protocols()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["TurboKestrel:Endpoints:Api:Url"] = "http://localhost:5000",
            ["TurboKestrel:Endpoints:Api:Protocols"] = "Http2"
        });

        var options = new TurboServerOptions();
        TurboKestrelConfigurationBinder.Bind(options, config.GetSection("TurboKestrel"));

        Assert.Single(options.ListenOptions);
        Assert.Equal(HttpProtocols.Http2, options.ListenOptions[0].Protocols);
    }

    [Fact(Timeout = 5000)]
    public void Bind_should_parse_https_defaults()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["TurboKestrel:HttpsDefaults:SslProtocols"] = "Tls13",
            ["TurboKestrel:HttpsDefaults:HandshakeTimeout"] = "00:00:30"
        });

        var options = new TurboServerOptions();
        TurboKestrelConfigurationBinder.Bind(options, config.GetSection("TurboKestrel"));

        Assert.NotNull(options.HttpsDefaultsCallback);

        var httpsOptions = new TurboHttpsOptions();
        options.HttpsDefaultsCallback!(httpsOptions);
        Assert.Equal(SslProtocols.Tls13, httpsOptions.EnabledSslProtocols);
        Assert.Equal(TimeSpan.FromSeconds(30), httpsOptions.HandshakeTimeout);
    }

    [Fact(Timeout = 5000)]
    public void Bind_should_parse_multiple_endpoints()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["TurboKestrel:Endpoints:Http:Url"] = "http://localhost:5000",
            ["TurboKestrel:Endpoints:Api:Url"] = "http://localhost:6000"
        });

        var options = new TurboServerOptions();
        TurboKestrelConfigurationBinder.Bind(options, config.GetSection("TurboKestrel"));

        Assert.Equal(2, options.Urls.Count);
    }

    [Fact(Timeout = 5000)]
    public void Bind_should_ignore_missing_section()
    {
        var config = BuildConfig(new Dictionary<string, string?>());

        var options = new TurboServerOptions();
        TurboKestrelConfigurationBinder.Bind(options, config.GetSection("TurboKestrel"));

        Assert.Empty(options.Urls);
        Assert.Empty(options.ListenOptions);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
