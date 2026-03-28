---
title: "11.6.  Authenticating Users to Origin Servers"
rfc_number: 9110
rfc_section: "11.6"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 11.6: Authenticating Users to Origin Servers — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, authenticating_users_to_origin_servers]
---

## 11.6.  Authenticating Users to Origin Servers

## 11.6  Authenticating Users to Origin Servers

### 11.6.1  WWW-Authenticate

   The "WWW-Authenticate" response header field indicates the
   authentication scheme(s) and parameters applicable to the target
   resource.


```abnf
     WWW-Authenticate = #challenge
```


> **MUST**: A server generating a 401 (Unauthorized) response MUST send a WWW-
   Authenticate header field containing at least one challenge.  A
> **MAY**: server MAY generate a WWW-Authenticate header field in other response
   messages to indicate that supplying credentials (or different
   credentials) might affect the response.

> **MUST NOT**: A proxy forwarding a response MUST NOT modify any WWW-Authenticate
   header fields in that response.

   User agents are advised to take special care in parsing the field
   value, as it might contain more than one challenge, and each
   challenge can contain a comma-separated list of authentication
   parameters.  Furthermore, the header field itself can occur multiple
   times.

   For instance:

   WWW-Authenticate: Basic realm="simple", Newauth realm="apps",
                    type=1, title="Login to \"apps\""

   This header field contains two challenges, one for the "Basic" scheme
   with a realm value of "simple" and another for the "Newauth" scheme
   with a realm value of "apps".  It also contains two additional
   parameters, "type" and "title".

   Some user agents do not recognize this form, however.  As a result,
   sending a WWW-Authenticate field value with more than one member on
   the same field line might not be interoperable.

      |  *Note:* The challenge grammar production uses the list syntax
      |  as well.  Therefore, a sequence of comma, whitespace, and comma
      |  can be considered either as applying to the preceding
      |  challenge, or to be an empty entry in the list of challenges.
      |  In practice, this ambiguity does not affect the semantics of
      |  the header field value and thus is harmless.

### 11.6.2  Authorization

   The "Authorization" header field allows a user agent to authenticate
   itself with an origin server -- usually, but not necessarily, after
   receiving a 401 (Unauthorized) response.  Its value consists of
   credentials containing the authentication information of the user
   agent for the realm of the resource being requested.


```abnf
     Authorization = credentials
```


   If a request is authenticated and a realm specified, the same
   credentials are presumed to be valid for all other requests within
   this realm (assuming that the authentication scheme itself does not
   require otherwise, such as credentials that vary according to a
   challenge value or using synchronized clocks).

> **MUST NOT**: A proxy forwarding a request MUST NOT modify any Authorization header
   fields in that request.  See Section 3.5 of [CACHING] for details of
   and requirements pertaining to handling of the Authorization header
   field by HTTP caches.

### 11.6.3  Authentication-Info

   HTTP authentication schemes can use the "Authentication-Info"
   response field to communicate information after the client's
   authentication credentials have been accepted.  This information can
   include a finalization message from the server (e.g., it can contain
   the server authentication).

   The field value is a list of parameters (name/value pairs), using the
   "auth-param" syntax defined in Section 11.3.  This specification only
   describes the generic format; authentication schemes using
   Authentication-Info will define the individual parameters.  The
   "Digest" Authentication Scheme, for instance, defines multiple
   parameters in Section 3.5 of [RFC7616].


```abnf
     Authentication-Info = #auth-param
```


   The Authentication-Info field can be used in any HTTP response,
   independently of request method and status code.  Its semantics are
   defined by the authentication scheme indicated by the Authorization
   header field (Section 11.6.2) of the corresponding request.

   A proxy forwarding a response is not allowed to modify the field
   value in any way.

   Authentication-Info can be sent as a trailer field (Section 6.5) when
   the authentication scheme explicitly allows this.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
