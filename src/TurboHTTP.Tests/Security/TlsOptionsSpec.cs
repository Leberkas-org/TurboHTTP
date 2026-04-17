using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Tests.Security;

/// <summary>
/// Tests sensitive header stripping on scheme downgrade / cross-origin redirects, and
/// TLS options propagation through the options pipeline. Companion to
/// <see cref="TlsSecuritySpec"/> which covers certificate validation and redirect protection.
/// </summary>
/// <remarks>
/// Classes under test: <see cref="TurboClientOptions"/>, <see cref="OptionsFactory"/>,
/// <see cref="RedirectHandler"/>, <see cref="RedirectPolicy"/>.
/// Attack vectors: credential leakage on cross-origin or scheme-change redirect,
/// TLS option misconfiguration.
/// </remarks>
public sealed class TlsOptionsSpec
{
    private static RequestEndpoint ToEndpoint(Uri uri, Version? version = null)
    {
        return new RequestEndpoint
        {
            Host = uri.Host,
            Port = (ushort)(uri.IsDefaultPort ? 0 : uri.Port),
            Scheme = uri.Scheme,
            Version = version ?? HttpVersion.Version11
        };
    }

    [Fact(Timeout = 5000)]
    public void RedirectHandler_should_strip_authorization_header_when_cross_origin_redirect()
    {
        // Attack: Authorization header forwarded to a different origin exposes credentials.
        var policy = new RedirectPolicy { AllowHttpsToHttpDowngrade = true };
        var handler = new RedirectHandler(policy);

        var original = new HttpRequestMessage(HttpMethod.Get, "https://trusted.com/api");
        original.Headers.TryAddWithoutValidation("Authorization", "Bearer secret-token");
        original.Headers.TryAddWithoutValidation("Accept", "application/json");

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.Found);
        response.Headers.Location = new Uri("http://evil.com/steal");

        var newRequest = handler.BuildRedirectRequest(original, response);

        // Authorization must be stripped (cross-origin)
        Assert.DoesNotContain(newRequest.Headers, h =>
            h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
        // Non-sensitive headers should be preserved
        Assert.Contains(newRequest.Headers, h =>
            h.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Timeout = 5000)]
    public void RedirectHandler_should_strip_cookie_header_when_redirect()
    {
        // Cookies must never be blindly forwarded on redirect — they must be re-evaluated
        // via the CookieJar against the new URI's domain/path/Secure rules.
        var handler = new RedirectHandler();

        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page");
        original.Headers.TryAddWithoutValidation("Cookie", "session=abc123");

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.Found);
        response.Headers.Location = new Uri("https://example.com/other");

        var newRequest = handler.BuildRedirectRequest(original, response);

        // Cookie header must be stripped (requires CookieJar re-evaluation)
        Assert.DoesNotContain(newRequest.Headers, h =>
            h.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Timeout = 5000)]
    public void RedirectHandler_should_strip_authorization_when_scheme_changes_from_https_to_http()
    {
        // A scheme change (https → http) is cross-origin, so Authorization must be stripped.
        var policy = new RedirectPolicy { AllowHttpsToHttpDowngrade = true };
        var handler = new RedirectHandler(policy);

        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api");
        original.Headers.TryAddWithoutValidation("Authorization", "Basic dXNlcjpwYXNz");

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.Found);
        response.Headers.Location = new Uri("http://example.com/api");

        var newRequest = handler.BuildRedirectRequest(original, response);

        // Scheme change makes it cross-origin → Authorization stripped
        Assert.DoesNotContain(newRequest.Headers, h =>
            h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Timeout = 5000)]
    public void RedirectHandler_should_preserve_authorization_when_same_origin_https_redirect()
    {
        var handler = new RedirectHandler();

        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/old");
        original.Headers.TryAddWithoutValidation("Authorization", "Bearer keep-me");

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.Found);
        response.Headers.Location = new Uri("https://example.com/new");

        var newRequest = handler.BuildRedirectRequest(original, response);

        // Same origin (same scheme + host + port) → Authorization preserved
        Assert.Contains(newRequest.Headers, h =>
            h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Timeout = 5000)]
    public void RedirectHandler_should_strip_authorization_when_host_changes()
    {
        var handler = new RedirectHandler();

        var original = new HttpRequestMessage(HttpMethod.Get, "https://trusted.com/api");
        original.Headers.TryAddWithoutValidation("Authorization", "Bearer secret");

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.Found);
        response.Headers.Location = new Uri("https://other.com/api");

        var newRequest = handler.BuildRedirectRequest(original, response);

        Assert.DoesNotContain(newRequest.Headers, h =>
            h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Timeout = 5000)]
    public void RedirectHandler_should_strip_authorization_when_port_changes()
    {
        var handler = new RedirectHandler();

        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com:443/api");
        original.Headers.TryAddWithoutValidation("Authorization", "Bearer secret");

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.Found);
        response.Headers.Location = new Uri("https://example.com:8443/api");

        var newRequest = handler.BuildRedirectRequest(original, response);

        Assert.DoesNotContain(newRequest.Headers, h =>
            h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Timeout = 5000)]
    public void TcpOptionsFactory_should_set_target_host_when_https_uri()
    {
        var options = new TurboClientOptions();
        var uri = new Uri("https://secure.example.com/path");

        var tcpOptions = OptionsFactory.Build(ToEndpoint(uri), options);
        var tlsOptions = Assert.IsType<TlsOptions>(tcpOptions);

        Assert.Equal("secure.example.com", tlsOptions.TargetHost);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptionsFactory_should_propagate_client_certificates_when_configured()
    {
        var certs = new X509CertificateCollection();
        var options = new TurboClientOptions
        {
            ClientCertificates = certs,
        };

        var uri = new Uri("https://mtls.example.com/");
        var tcpOptions = OptionsFactory.Build(ToEndpoint(uri), options);
        var tlsOptions = Assert.IsType<TlsOptions>(tcpOptions);

        Assert.Same(certs, tlsOptions.ClientCertificates);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptionsFactory_should_propagate_enabled_ssl_protocols_when_configured()
    {
        var options = new TurboClientOptions
        {
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
        };

        var uri = new Uri("https://example.com/");
        var tcpOptions = OptionsFactory.Build(ToEndpoint(uri), options);
        var tlsOptions = Assert.IsType<TlsOptions>(tcpOptions);

        Assert.Equal(SslProtocols.Tls12 | SslProtocols.Tls13, tlsOptions.EnabledSslProtocols);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptionsFactory_should_default_to_none_ssl_protocol_when_not_configured()
    {
        var options = new TurboClientOptions();
        var uri = new Uri("https://example.com/");

        var tcpOptions = OptionsFactory.Build(ToEndpoint(uri), options);
        var tlsOptions = Assert.IsType<TlsOptions>(tcpOptions);

        // SslProtocols.None lets the OS negotiate the best available protocol
        Assert.Equal(SslProtocols.None, tlsOptions.EnabledSslProtocols);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptionsFactory_should_produce_plain_tcp_options_when_http_uri()
    {
        var options = new TurboClientOptions();
        var uri = new Uri("http://example.com/");

        var tcpOptions = OptionsFactory.Build(ToEndpoint(uri), options);

        Assert.IsType<TcpOptions>(tcpOptions);
        Assert.IsNotType<TlsOptions>(tcpOptions);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptionsFactory_should_produce_tls_options_when_wss_uri()
    {
        var options = new TurboClientOptions();
        var uri = new Uri("wss://ws.example.com/");

        var tcpOptions = OptionsFactory.Build(ToEndpoint(uri), options);

        Assert.IsType<TlsOptions>(tcpOptions);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptionsFactory_should_set_correct_port_when_https_with_custom_port()
    {
        var options = new TurboClientOptions();
        var uri = new Uri("https://example.com:8443/");

        var tcpOptions = OptionsFactory.Build(ToEndpoint(uri), options);
        var tlsOptions = Assert.IsType<TlsOptions>(tcpOptions);

        Assert.Equal(8443, tlsOptions.Port);
        Assert.Equal("example.com", tlsOptions.TargetHost);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptionsFactory_should_propagate_validation_callback_when_http3_request()
    {
        var invoked = false;
        var options = new TurboClientOptions
        {
            ServerCertificateValidationCallback = (_, _, _, _) =>
            {
                invoked = true;
                return true;
            },
        };

        var uri = new Uri("https://example.com/");
        var tcpOptions = OptionsFactory.Build(ToEndpoint(uri, new Version(3, 0)), options);
        var quicOptions = Assert.IsType<QuicOptions>(tcpOptions);

        Assert.NotNull(quicOptions.ServerCertificateValidationCallback);
        quicOptions.ServerCertificateValidationCallback!(null!, null, null, SslPolicyErrors.None);
        Assert.True(invoked);
    }

    [Fact(Timeout = 5000)]
    public void TurboClientOptions_should_have_null_client_certificates_when_default_options()
    {
        var options = new TurboClientOptions();

        Assert.Null(options.ClientCertificates);

        var uri = new Uri("https://example.com/");
        var tcpOptions = OptionsFactory.Build(ToEndpoint(uri), options);
        var tlsOptions = Assert.IsType<TlsOptions>(tcpOptions);

        Assert.Null(tlsOptions.ClientCertificates);
    }
}
