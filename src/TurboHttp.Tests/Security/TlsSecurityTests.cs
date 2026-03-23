using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Transport;

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
public sealed class TlsSecurityTests
{
    // ══════════════════════════════════════════════════════════════════════════════
    // Default Certificate Validation — Rejects Self-Signed / Untrusted Certs
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-TLS-001: Default callback rejects self-signed cert (chain errors) — untrusted CA attack")]
    public void Should_RejectSelfSignedCert_When_DefaultValidation()
    {
        // Attack: A self-signed certificate presents SslPolicyErrors.RemoteCertificateChainErrors.
        // Default configuration must reject it.
        var options = new TurboClientOptions();
        var callback = options.ServerCertificateValidationCallback;

        Assert.NotNull(callback);
        Assert.False(callback!(null!, null, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact(DisplayName = "SEC-TLS-002: Default callback rejects cert name mismatch — MITM domain spoofing")]
    public void Should_RejectCertNameMismatch_When_DefaultValidation()
    {
        // Attack: Attacker presents a valid cert for a different domain (MITM).
        var options = new TurboClientOptions();
        var callback = options.ServerCertificateValidationCallback!;

        Assert.False(callback(null!, null, null, SslPolicyErrors.RemoteCertificateNameMismatch));
    }

    [Fact(DisplayName = "SEC-TLS-003: Default callback rejects missing certificate — TLS stripping")]
    public void Should_RejectMissingCert_When_DefaultValidation()
    {
        // Attack: No certificate presented during handshake.
        var options = new TurboClientOptions();
        var callback = options.ServerCertificateValidationCallback!;

        Assert.False(callback(null!, null, null, SslPolicyErrors.RemoteCertificateNotAvailable));
    }

    [Fact(DisplayName = "SEC-TLS-004: Default callback rejects combined policy errors — multiple failures")]
    public void Should_RejectCombinedErrors_When_DefaultValidation()
    {
        // Attack: Multiple validation failures simultaneously (e.g. self-signed + wrong name).
        var options = new TurboClientOptions();
        var callback = options.ServerCertificateValidationCallback!;

        var combined = SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch;
        Assert.False(callback(null!, null, null, combined));
    }

    [Fact(DisplayName = "SEC-TLS-005: Default callback accepts valid certificate — no false positives")]
    public void Should_AcceptValidCert_When_DefaultValidation()
    {
        var options = new TurboClientOptions();
        var callback = options.ServerCertificateValidationCallback!;

        Assert.True(callback(null!, null, null, SslPolicyErrors.None));
    }

    [Fact(DisplayName = "SEC-TLS-006: DangerousAcceptAny flag is false by default — secure default posture")]
    public void Should_DisableDangerousAcceptAny_When_DefaultOptions()
    {
        var options = new TurboClientOptions();

        Assert.False(options.DangerousAcceptAnyServerCertificate);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Custom Validation Callback
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-TLS-007: Custom validation callback is invoked — callback propagation")]
    public void Should_InvokeCustomCallback_When_Configured()
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

    [Fact(DisplayName = "SEC-TLS-008: Custom callback decision is respected — allows custom trust stores")]
    public void Should_RespectCustomCallbackDecision_When_Accepting()
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

    [Fact(DisplayName = "SEC-TLS-009: DangerousAcceptAny overrides custom callback — bypasses all validation")]
    public void Should_BypassCustomCallback_When_DangerousAcceptAnyIsTrue()
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

    [Fact(DisplayName = "SEC-TLS-010: Custom callback propagated through TcpOptionsFactory — end-to-end wiring")]
    public void Should_PropagateCustomCallback_When_BuildingTlsOptions()
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

    // ══════════════════════════════════════════════════════════════════════════════
    // HTTPS → HTTP Redirect Protection (Cross-Scheme Downgrade)
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-TLS-011: HTTPS→HTTP redirect blocked by default — protocol downgrade attack")]
    public void Should_BlockHttpsToHttpRedirect_When_DefaultPolicy()
    {
        // Attack: Attacker forces redirect from HTTPS to HTTP to intercept traffic in plaintext.
        var handler = new RedirectHandler(); // Default policy: AllowHttpsToHttpDowngrade = false

        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure");
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.Found);
        response.Headers.Location = new Uri("http://evil.com/steal");

        var ex = Assert.Throws<RedirectException>(() => handler.BuildRedirectRequest(original, response));
        Assert.Equal(RedirectError.ProtocolDowngrade, ex.Error);
    }

    [Fact(DisplayName = "SEC-TLS-012: HTTPS→HTTP redirect blocked for 301 — permanent redirect downgrade")]
    public void Should_BlockHttpsToHttpRedirect_When_301MovedPermanently()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page");
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.MovedPermanently);
        response.Headers.Location = new Uri("http://example.com/page");

        var ex = Assert.Throws<RedirectException>(() => handler.BuildRedirectRequest(original, response));
        Assert.Equal(RedirectError.ProtocolDowngrade, ex.Error);
    }

    [Fact(DisplayName = "SEC-TLS-013: HTTPS→HTTP redirect blocked for 307 — temporary redirect downgrade")]
    public void Should_BlockHttpsToHttpRedirect_When_307TemporaryRedirect()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api");
        original.Content = new ByteArrayContent("data"u8.ToArray());
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.TemporaryRedirect);
        response.Headers.Location = new Uri("http://example.com/api");

        var ex = Assert.Throws<RedirectException>(() => handler.BuildRedirectRequest(original, response));
        Assert.Equal(RedirectError.ProtocolDowngrade, ex.Error);
    }

    [Fact(DisplayName = "SEC-TLS-014: HTTPS→HTTP redirect blocked for 308 — permanent redirect downgrade")]
    public void Should_BlockHttpsToHttpRedirect_When_308PermanentRedirect()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.PermanentRedirect);
        response.Headers.Location = new Uri("http://example.com/resource");

        var ex = Assert.Throws<RedirectException>(() => handler.BuildRedirectRequest(original, response));
        Assert.Equal(RedirectError.ProtocolDowngrade, ex.Error);
    }

    [Fact(DisplayName = "SEC-TLS-015: HTTPS→HTTPS redirect allowed — same-scheme is safe")]
    public void Should_AllowHttpsToHttpsRedirect_When_SameScheme()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/old");
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.Found);
        response.Headers.Location = new Uri("https://example.com/new");

        var newRequest = handler.BuildRedirectRequest(original, response);

        Assert.Equal("https", newRequest.RequestUri!.Scheme);
    }

    [Fact(DisplayName = "SEC-TLS-016: HTTP→HTTPS redirect allowed — upgrade is safe")]
    public void Should_AllowHttpToHttpsRedirect_When_Upgrading()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.Found);
        response.Headers.Location = new Uri("https://example.com/page");

        var newRequest = handler.BuildRedirectRequest(original, response);

        Assert.Equal("https", newRequest.RequestUri!.Scheme);
    }

    [Fact(DisplayName = "SEC-TLS-017: HTTPS→HTTP allowed when policy explicitly permits — opt-in downgrade")]
    public void Should_AllowHttpsToHttpRedirect_When_PolicyPermits()
    {
        var policy = new RedirectPolicy { AllowHttpsToHttpDowngrade = true };
        var handler = new RedirectHandler(policy);

        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page");
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.Found);
        response.Headers.Location = new Uri("http://example.com/page");

        var newRequest = handler.BuildRedirectRequest(original, response);

        Assert.Equal("http", newRequest.RequestUri!.Scheme);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Sensitive Header Stripping on Scheme Downgrade / Cross-Origin
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-TLS-018: Authorization header stripped on cross-origin redirect — credential leakage prevention")]
    public void Should_StripAuthorizationHeader_When_CrossOriginRedirect()
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

    [Fact(DisplayName = "SEC-TLS-019: Cookie header stripped on redirect — cookie re-evaluation required")]
    public void Should_StripCookieHeader_When_Redirect()
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

    [Fact(DisplayName = "SEC-TLS-020: Authorization stripped on scheme-change redirect — HTTPS→HTTP credential leak")]
    public void Should_StripAuthorization_When_SchemeChangesFromHttpsToHttp()
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

    [Fact(DisplayName = "SEC-TLS-021: Authorization preserved on same-origin HTTPS redirect — no unnecessary stripping")]
    public void Should_PreserveAuthorization_When_SameOriginHttpsRedirect()
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

    [Fact(DisplayName = "SEC-TLS-022: Authorization stripped on host-change redirect — cross-host credential leak")]
    public void Should_StripAuthorization_When_HostChanges()
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

    [Fact(DisplayName = "SEC-TLS-023: Authorization stripped on port-change redirect — cross-port credential leak")]
    public void Should_StripAuthorization_When_PortChanges()
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

    // ══════════════════════════════════════════════════════════════════════════════
    // TLS Options Propagation (TargetHost, ClientCertificates, EnabledSslProtocols)
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-TLS-024: TargetHost set from URI host for HTTPS — SNI correctness")]
    public void Should_SetTargetHost_When_HttpsUri()
    {
        var options = new TurboClientOptions();
        var uri = new Uri("https://secure.example.com/path");

        var tcpOptions = TcpOptionsFactory.Build(uri, options);
        var tlsOptions = Assert.IsType<TlsOptions>(tcpOptions);

        Assert.Equal("secure.example.com", tlsOptions.TargetHost);
    }

    [Fact(DisplayName = "SEC-TLS-025: ClientCertificates propagated to TlsOptions — mutual TLS support")]
    public void Should_PropagateClientCertificates_When_Configured()
    {
        var certs = new X509CertificateCollection();
        var options = new TurboClientOptions
        {
            ClientCertificates = certs,
        };

        var uri = new Uri("https://mtls.example.com/");
        var tcpOptions = TcpOptionsFactory.Build(uri, options);
        var tlsOptions = Assert.IsType<TlsOptions>(tcpOptions);

        Assert.Same(certs, tlsOptions.ClientCertificates);
    }

    [Fact(DisplayName = "SEC-TLS-026: EnabledSslProtocols propagated to TlsOptions — protocol pinning")]
    public void Should_PropagateEnabledSslProtocols_When_Configured()
    {
        var options = new TurboClientOptions
        {
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
        };

        var uri = new Uri("https://example.com/");
        var tcpOptions = TcpOptionsFactory.Build(uri, options);
        var tlsOptions = Assert.IsType<TlsOptions>(tcpOptions);

        Assert.Equal(SslProtocols.Tls12 | SslProtocols.Tls13, tlsOptions.EnabledSslProtocols);
    }

    [Fact(DisplayName = "SEC-TLS-027: Default SslProtocols is None (OS-negotiated) — no weak protocol pinning")]
    public void Should_DefaultToNoneSslProtocol_When_NotConfigured()
    {
        var options = new TurboClientOptions();
        var uri = new Uri("https://example.com/");

        var tcpOptions = TcpOptionsFactory.Build(uri, options);
        var tlsOptions = Assert.IsType<TlsOptions>(tcpOptions);

        // SslProtocols.None lets the OS negotiate the best available protocol
        Assert.Equal(SslProtocols.None, tlsOptions.EnabledSslProtocols);
    }

    [Fact(DisplayName = "SEC-TLS-028: HTTP URI does not produce TLS options — no unnecessary TLS")]
    public void Should_ProducePlainTcpOptions_When_HttpUri()
    {
        var options = new TurboClientOptions();
        var uri = new Uri("http://example.com/");

        var tcpOptions = TcpOptionsFactory.Build(uri, options);

        Assert.IsType<TcpOptions>(tcpOptions);
        Assert.IsNotType<TlsOptions>(tcpOptions);
    }

    [Fact(DisplayName = "SEC-TLS-029: WSS URI produces TLS options — WebSocket secure transport")]
    public void Should_ProduceTlsOptions_When_WssUri()
    {
        var options = new TurboClientOptions();
        var uri = new Uri("wss://ws.example.com/");

        var tcpOptions = TcpOptionsFactory.Build(uri, options);

        Assert.IsType<TlsOptions>(tcpOptions);
    }

    [Fact(DisplayName = "SEC-TLS-030: HTTPS URI with custom port produces correct TLS options — non-standard port")]
    public void Should_SetCorrectPort_When_HttpsWithCustomPort()
    {
        var options = new TurboClientOptions();
        var uri = new Uri("https://example.com:8443/");

        var tcpOptions = TcpOptionsFactory.Build(uri, options);
        var tlsOptions = Assert.IsType<TlsOptions>(tcpOptions);

        Assert.Equal(8443, tlsOptions.Port);
        Assert.Equal("example.com", tlsOptions.TargetHost);
    }

    [Fact(DisplayName = "SEC-TLS-031: HTTP/3 URI produces QuicOptions with cert callback — QUIC TLS validation")]
    public void Should_PropagateValidationCallback_When_Http3Request()
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
        var tcpOptions = TcpOptionsFactory.Build(uri, options, new Version(3, 0));
        var quicOptions = Assert.IsType<QuicOptions>(tcpOptions);

        Assert.NotNull(quicOptions.ServerCertificateValidationCallback);
        quicOptions.ServerCertificateValidationCallback!(null!, null, null, SslPolicyErrors.None);
        Assert.True(invoked);
    }

    [Fact(DisplayName = "SEC-TLS-032: ClientCertificates null by default — no accidental client cert")]
    public void Should_HaveNullClientCertificates_When_DefaultOptions()
    {
        var options = new TurboClientOptions();

        Assert.Null(options.ClientCertificates);

        var uri = new Uri("https://example.com/");
        var tcpOptions = TcpOptionsFactory.Build(uri, options);
        var tlsOptions = Assert.IsType<TlsOptions>(tcpOptions);

        Assert.Null(tlsOptions.ClientCertificates);
    }
}
