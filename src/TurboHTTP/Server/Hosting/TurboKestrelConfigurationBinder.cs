using System.Net;
using System.Security.Authentication;
using Microsoft.Extensions.Configuration;

namespace TurboHTTP.Server.Hosting;

internal static class TurboKestrelConfigurationBinder
{
    public static void Bind(TurboServerOptions options, IConfigurationSection section)
    {
        if (!section.Exists())
        {
            return;
        }

        BindHttpsDefaults(options, section.GetSection("HttpsDefaults"));
        BindEndpoints(options, section.GetSection("Endpoints"));
    }

    private static void BindHttpsDefaults(TurboServerOptions options, IConfigurationSection section)
    {
        if (!section.Exists())
        {
            return;
        }

        var sslProtocols = ParseSslProtocols(section["SslProtocols"]);
        var handshakeTimeout = ParseTimeSpan(section["HandshakeTimeout"]);

        options.ConfigureHttpsDefaults(https =>
        {
            if (sslProtocols != SslProtocols.None)
            {
                https.EnabledSslProtocols = sslProtocols;
            }

            if (handshakeTimeout.HasValue)
            {
                https.HandshakeTimeout = handshakeTimeout.Value;
            }
        });
    }

    private static void BindEndpoints(TurboServerOptions options, IConfigurationSection section)
    {
        if (!section.Exists())
        {
            return;
        }

        foreach (var endpoint in section.GetChildren())
        {
            var url = endpoint["Url"];
            if (url is null)
            {
                continue;
            }

            var certSection = endpoint.GetSection("Certificate");
            var hasCert = certSection.Exists() && certSection["Path"] is not null;
            var hasSslProtocols = endpoint["SslProtocols"] is not null;
            var hasProtocols = endpoint["Protocols"] is not null;

            if (!hasCert && !hasSslProtocols && !hasProtocols)
            {
                options.Urls.Add(url);
                continue;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                options.Urls.Add(url);
                continue;
            }

            var host = uri.Host;
            IPAddress address;

            if (host == "*" || host == "+")
            {
                address = IPAddress.Any;
            }
            else if (IPAddress.TryParse(host, out var parsed))
            {
                address = parsed;
            }
            else if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                address = IPAddress.Loopback;
            }
            else
            {
                address = IPAddress.Any;
            }

            var port = (ushort)uri.Port;
            var protocols = ParseHttpProtocols(endpoint["Protocols"]);
            var sslProtocols = ParseSslProtocols(endpoint["SslProtocols"]);

            options.Listen(address, port, listen =>
            {
                if (protocols != HttpProtocols.None)
                {
                    listen.Protocols = protocols;
                }

                if (uri.Scheme == "https")
                {
                    if (hasCert)
                    {
                        listen.UseHttps(certSection["Path"]!, certSection["Password"], https =>
                        {
                            if (sslProtocols != SslProtocols.None)
                            {
                                https.EnabledSslProtocols = sslProtocols;
                            }
                        });
                    }
                    else
                    {
                        listen.UseHttps(https =>
                        {
                            if (sslProtocols != SslProtocols.None)
                            {
                                https.EnabledSslProtocols = sslProtocols;
                            }
                        });
                    }
                }
            });
        }
    }

    private static SslProtocols ParseSslProtocols(string? value)
    {
        if (value is null)
        {
            return SslProtocols.None;
        }

        return Enum.Parse<SslProtocols>(value, ignoreCase: true);
    }

    private static HttpProtocols ParseHttpProtocols(string? value)
    {
        if (value is null)
        {
            return HttpProtocols.None;
        }

        return Enum.Parse<HttpProtocols>(value, ignoreCase: true);
    }

    private static TimeSpan? ParseTimeSpan(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return TimeSpan.Parse(value);
    }
}
