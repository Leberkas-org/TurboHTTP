---
title: "11.  Access Authentication"
rfc_number: 1945
rfc_section: "11"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 11: Access Authentication — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request, access_authentication]
---

# 11.  Access Authentication


   HTTP provides a simple challenge-response authentication mechanism
   which may be used by a server to challenge a client request and by a
   client to provide authentication information. It uses an extensible,
   case-insensitive token to identify the authentication scheme,
   followed by a comma-separated list of attribute-value pairs which
   carry the parameters necessary for achieving authentication via that
   scheme.


```abnf
       auth-scheme    = token

       auth-param     = token "=" quoted-string
```


   The 401 (unauthorized) response message is used by an origin server
   to challenge the authorization of a user agent. This response must
   include a WWW-Authenticate header field containing at least one
   challenge applicable to the requested resource.


```abnf
       challenge      = auth-scheme 1*SP realm *( "," auth-param )

       realm          = "realm" "=" realm-value
       realm-value    = quoted-string
```


   The realm attribute (case-insensitive) is required for all
   authentication schemes which issue a challenge. The realm value
   (case-sensitive), in combination with the canonical root URL of the
   server being accessed, defines the protection space. These realms
   allow the protected resources on a server to be partitioned into a
   set of protection spaces, each with its own authentication scheme
   and/or authorization database. The realm value is a string, generally
   assigned by the origin server, which may have additional semantics
   specific to the authentication scheme.

   A user agent that wishes to authenticate itself with a server--
   usually, but not necessarily, after receiving a 401 response--may do
   so by including an Authorization header field with the request. The
   Authorization field value consists of credentials containing the
   authentication information of the user agent for the realm of the
   resource being requested.


```abnf
       credentials    = basic-credentials
                      | ( auth-scheme #auth-param )
```


   The domain over which credentials can be automatically applied by a
   user agent is determined by the protection space. If a prior request
   has been authorized, the same credentials may be reused for all other
   requests within that protection space for a period of time determined



   by the authentication scheme, parameters, and/or user preference.
   Unless otherwise defined by the authentication scheme, a single
   protection space cannot extend outside the scope of its server.

   If the server does not wish to accept the credentials sent with a
   request, it should return a 403 (forbidden) response.

   The HTTP protocol does not restrict applications to this simple
   challenge-response mechanism for access authentication. Additional
   mechanisms may be used, such as encryption at the transport level or
   via message encapsulation, and with additional header fields
   specifying authentication information. However, these additional
   mechanisms are not defined by this specification.

   Proxies must be completely transparent regarding user agent
   authentication. That is, they must forward the WWW-Authenticate and
   Authorization headers untouched, and must not cache the response to a
   request containing Authorization. HTTP/1.0 does not provide a means
   for a client to be authenticated with a proxy.

## 11.1  Basic Authentication Scheme

   The "basic" authentication scheme is based on the model that the user
   agent must authenticate itself with a user-ID and a password for each
   realm. The realm value should be considered an opaque string which
   can only be compared for equality with other realms on that server.
   The server will authorize the request only if it can validate the
   user-ID and password for the protection space of the Request-URI.
   There are no optional authentication parameters.

   Upon receipt of an unauthorized request for a URI within the
   protection space, the server should respond with a challenge like the
   following:

       WWW-Authenticate: Basic realm="WallyWorld"

   where "WallyWorld" is the string assigned by the server to identify
   the protection space of the Request-URI.

   To receive authorization, the client sends the user-ID and password,
   separated by a single colon (":") character, within a base64 [5]
   encoded string in the credentials.


```abnf
       basic-credentials = "Basic" SP basic-cookie

       basic-cookie      = <base64 [5] encoding of userid-password,
                            except not limited to 76 char/line>
```






```abnf
       userid-password   = [ token ] ":" *TEXT
```


   If the user agent wishes to send the user-ID "Aladdin" and password
   "open sesame", it would use the following header field:

       Authorization: Basic QWxhZGRpbjpvcGVuIHNlc2FtZQ==

   The basic authentication scheme is a non-secure method of filtering
   unauthorized access to resources on an HTTP server. It is based on
   the assumption that the connection between the client and the server
   can be regarded as a trusted carrier. As this is not generally true
   on an open network, the basic authentication scheme should be used
   accordingly. In spite of this, clients should implement the scheme in
   order to communicate with servers that use it.

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
