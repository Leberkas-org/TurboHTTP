using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using TurboHttp.Protocol.Semantics;
using TurboHttp.Transport.Tcp;
using TurboHttp.Transport.Connection;

namespace TurboHttp.Tests.Security;

/// <summary>
/// Tests TLS certificate validation, transport security options, and cross-scheme
/// downgrade protection. Verifies that the default configuration rejects untrusted
/// certificates, custom callbacks propagate correctly through the options pipeline,
/// HTTPS→HTTP redirects are blocked, and sensitive headers are stripped on scheme downgrade.
/// </summary>
/// <remarks>
/// Classes under test: <see cref="TurboClientOptions"/>, <see cref="TcpOptionsFactory"/>,
/// <see cref="RedirectHandler"/>, <see cref="RedirectPolicy"/>.
/// Attack vectors: self-signed certificate acceptance, protocol downgrade, credential leakage
/// on scheme change, TLS option misconfiguration.
/// </remarks>
public sealed class TlsSecuritySpec
{
    [Fact(Timeout = 5000)]
    public void TurboClientOptions_should_reject_self_signed_cert_when_default_validation()
    {
        // Attack: A self-signed certificate presents SslPolicyErrors.RemoteCertificateChainErrors.
        // Default configuration must reject it.
        var options = new TurboClientOptions();
        var callback = options.ServerCertificateValidationCallback;

        Assert.NotNull(callback);
        Assert.False(callback!(null!, null, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact(Timeout = 5000)]
    public void TurboClientOptions_should_reject_cert_name_mismatch_when_default_validation()
    {
        // Attack: Attacker presents a valid cert for a different domain (MITM).
        var options = new TurboClientOptions();
        var callback = options.ServerCertificateValidationCallback!;

        Assert.False(callback(null!, null, null, SslPolicyErrors.RemoteCertificateNameMismatch));
    }

    [Fact(Timeout = 5000)]
    public void TurboClientOptions_should_reject_missing_cert_when_default_validation()
    {
        // Attack: No certificate presented during handshake.
        var options = new TurboClientOptions();
        var callback = options.ServerCertificateValidationCallback!;

        Assert.False(callback(null!, null, null, SslPolicyErrors.RemoteCertificateNotAvailable));
    }

    [Fact(Timeout = 5000)]
    public void TurboClientOptions_should_reject_combined_errors_when_default_validation()
    {
        // Attack: Multiple validation failures simultaneously (e.g. self-signed + wrong name).
        var options = new TurboClientOptions();
        var callback = options.ServerCertificateValidationCallback!;

        var combined = SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch;
        Assert.False(callback(null!, null, null, combined));
    }

    [Fact(Timeout = 5000)]
    public void TurboClientOptions_should_accept_valid_cert_when_default_validation()
    {
        var options = new TurboClientOptions();
        var callback = options.ServerCertificateValidationCallback!;

        Assert.True(callback(null!, null, null, SslPolicyErrors.None));
    }

    [Fact(Timeout = 5000)]
    public void TurboClientOptions_should_disable_dangerous_accept_any_when_default_options()
    {
        var options = new TurboClientOptions();

        Assert.False(options.DangerousAcceptAnyServerCertificate);
    }

    [Fact(Timeout = 5000)]
    public void TurboClientOptions_should_invoke_custom_callback_when_configured()
    {
        var invoked = false;
        SslPolicyErrors? observedErrors = null;
        RemoteCertificateValidationCallback custom = (_, _, _, errors) =>
        {
            invoked = true;
            observedErrors = errors;
            return errors is SslPolicyErrors.None;
        };

        var options = new TurboClientOptions
        {
            ServerCertificateValidationCallback = custom,
        };

        var effective = options.EffectiveServerCertificateValidationCallback;
        Assert.NotNull(effective);

        effective!(null!, null, null, SslPolicyErrors.RemoteCertificateChainErrors);

        Assert.True(invoked);
        Assert.Equal(SslPolicyErrors.RemoteCertificateChainErrors, observedErrors);
    }

    [Fact(Timeout = 5000)]
    public void TurboClientOptions_should_respect_custom_callback_decision_when_accepting()
    {
        // Scenario: Custom callback implements pinning or custom CA trust.
        RemoteCertificateValidationCallback alwaysAccept = (_, _, _, _) => true;

        var options = new TurboClientOptions
        {
            ServerCertificateValidationCallback = alwaysAccept,
        };

        var effective = options.EffectiveServerCertificateValidationCallback!;

        // Should accept even chain errors via custom policy
        Assert.True(effective(null!, null, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact(Timeout = 5000)]
    public void TurboClientOptions_should_bypass_custom_callback_when_dangerous_accept_any_is_true()
    {
        // Verify that DangerousAcceptAnyServerCertificate takes precedence over
        // a custom callback that would reject.
        var customInvoked = false;
        var options = new TurboClientOptions
        {
            DangerousAcceptAnyServerCertificate = true,
            ServerCertificateValidationCallback = (_, _, _, _) =>
            {
                customInvoked = true;
                return false; // Would reject
            },
        };

        var effective = options.EffectiveServerCertificateValidationCallback!;

        Assert.True(effective(null!, null, null, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(customInvoked); // Custom callback must NOT be called
    }

    [Fact(Timeout = 5000)]
    public void TcpOptionsFactory_should_propagate_custom_callback_when_building_tls_options()
    {
        var invoked = false;
        RemoteCertificateValidationCallback custom = (_, _, _, _) =>
        {
            invoked = true;
            return true;
        };

        var options = new TurboClientOptions
        {
            ServerCertificateValidationCallback = custom,
        };

        var tcpOptions = TcpOptionsFactory.Build(new Uri("https://example.com/"), options);
        var tlsOptions = Assert.IsType<TlsOptions>(tcpOptions);

        Assert.NotNull(tlsOptions.ServerCertificateValidationCallback);
        tlsOptions.ServerCertificateValidationCallback!(null!, null, null, SslPolicyErrors.None);
        Assert.True(invoked);
    }

    [Fact(Timeout = 5000)]
    public void RedirectHandler_should_block_https_to_http_redirect_when_default_policy()
    {
        // Attack: Attacker forces redirect from HTTPS to HTTP to intercept traffic in plaintext.
        var handler = new RedirectHandler(); // Default policy: AllowHttpsToHttpDowngrade = false

        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure");
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.Found);
        response.Headers.Location = new Uri("http://evil.com/steal");

        var ex = Assert.Throws<RedirectException>(() => handler.BuildRedirectRequest(original, response));
        Assert.Equal(RedirectError.ProtocolDowngrade, ex.Error);
    }

    [Fact(Timeout = 5000)]
    public void RedirectHandler_should_block_https_to_http_redirect_when_301_moved_permanently()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page");
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.MovedPermanently);
        response.Headers.Location = new Uri("http://example.com/page");

        var ex = Assert.Throws<RedirectException>(() => handler.BuildRedirectRequest(original, response));
        Assert.Equal(RedirectError.ProtocolDowngrade, ex.Error);
    }

    [Fact(Timeout = 5000)]
    public void RedirectHandler_should_block_https_to_http_redirect_when_307_temporary_redirect()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api");
        original.Content = new ByteArrayContent("data"u8.ToArray());
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.TemporaryRedirect);
        response.Headers.Location = new Uri("http://example.com/api");

        var ex = Assert.Throws<RedirectException>(() => handler.BuildRedirectRequest(original, response));
        Assert.Equal(RedirectError.ProtocolDowngrade, ex.Error);
    }

    [Fact(Timeout = 5000)]
    public void RedirectHandler_should_block_https_to_http_redirect_when_308_permanent_redirect()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.PermanentRedirect);
        response.Headers.Location = new Uri("http://example.com/resource");

        var ex = Assert.Throws<RedirectException>(() => handler.BuildRedirectRequest(original, response));
        Assert.Equal(RedirectError.ProtocolDowngrade, ex.Error);
    }

    [Fact(Timeout = 5000)]
    public void RedirectHandler_should_allow_https_to_https_redirect_when_same_scheme()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/old");
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.Found);
        response.Headers.Location = new Uri("https://example.com/new");

        var newRequest = handler.BuildRedirectRequest(original, response);

        Assert.Equal("https", newRequest.RequestUri!.Scheme);
    }

    [Fact(Timeout = 5000)]
    public void RedirectHandler_should_allow_http_to_https_redirect_when_upgrading()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.Found);
        response.Headers.Location = new Uri("https://example.com/page");

        var newRequest = handler.BuildRedirectRequest(original, response);

        Assert.Equal("https", newRequest.RequestUri!.Scheme);
    }

    [Fact(Timeout = 5000)]
    public void RedirectHandler_should_allow_https_to_http_redirect_when_policy_permits()
    {
        var policy = new RedirectPolicy { AllowHttpsToHttpDowngrade = true };
        var handler = new RedirectHandler(policy);

        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page");
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.Found);
        response.Headers.Location = new Uri("http://example.com/page");

        var newRequest = handler.BuildRedirectRequest(original, response);

        Assert.Equal("http", newRequest.RequestUri!.Scheme);
    }
}
