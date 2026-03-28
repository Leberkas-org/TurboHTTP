---
title: "17.16.  Authentication Considerations"
rfc_number: 9110
rfc_section: "17.16"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 17.16: Authentication Considerations — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, authentication_considerations]
---

## 17.16.  Authentication Considerations

## 17.16  Authentication Considerations

   Everything about the topic of HTTP authentication is a security
   consideration, so the list of considerations below is not exhaustive.
   Furthermore, it is limited to security considerations regarding the
   authentication framework, in general, rather than discussing all of
   the potential considerations for specific authentication schemes
   (which ought to be documented in the specifications that define those
   schemes).  Various organizations maintain topical information and
   links to current research on Web application security (e.g.,
   [OWASP]), including common pitfalls for implementing and using the
   authentication schemes found in practice.

### 17.16.1  Confidentiality of Credentials

   The HTTP authentication framework does not define a single mechanism
   for maintaining the confidentiality of credentials; instead, each
   authentication scheme defines how the credentials are encoded prior
   to transmission.  While this provides flexibility for the development
   of future authentication schemes, it is inadequate for the protection
   of existing schemes that provide no confidentiality on their own, or
   that do not sufficiently protect against replay attacks.
   Furthermore, if the server expects credentials that are specific to
   each individual user, the exchange of those credentials will have the
   effect of identifying that user even if the content within
   credentials remains confidential.

   HTTP depends on the security properties of the underlying transport-
   or session-level connection to provide confidential transmission of
   fields.  Services that depend on individual user authentication
   require a secured connection prior to exchanging credentials
   (Section 4.2.2).

### 17.16.2  Credentials and Idle Clients

   Existing HTTP clients and user agents typically retain authentication
   information indefinitely.  HTTP does not provide a mechanism for the
   origin server to direct clients to discard these cached credentials,
   since the protocol has no awareness of how credentials are obtained
   or managed by the user agent.  The mechanisms for expiring or
   revoking credentials can be specified as part of an authentication
   scheme definition.

   Circumstances under which credential caching can interfere with the
   application's security model include but are not limited to:

   *  Clients that have been idle for an extended period, following
      which the server might wish to cause the client to re-prompt the
      user for credentials.

   *  Applications that include a session termination indication (such
      as a "logout" or "commit" button on a page) after which the server
      side of the application "knows" that there is no further reason
      for the client to retain the credentials.

   User agents that cache credentials are encouraged to provide a
   readily accessible mechanism for discarding cached credentials under
   user control.

### 17.16.3  Protection Spaces

   Authentication schemes that solely rely on the "realm" mechanism for
   establishing a protection space will expose credentials to all
   resources on an origin server.  Clients that have successfully made
   authenticated requests with a resource can use the same
   authentication credentials for other resources on the same origin
   server.  This makes it possible for a different resource to harvest
   authentication credentials for other resources.

   This is of particular concern when an origin server hosts resources
   for multiple parties under the same origin (Section 11.5).  Possible
   mitigation strategies include restricting direct access to
   authentication credentials (i.e., not making the content of the
   Authorization request header field available), and separating
   protection spaces by using a different host name (or port number) for
   each party.

### 17.16.4  Additional Response Fields

   Adding information to responses that are sent over an unencrypted
   channel can affect security and privacy.  The presence of the
   Authentication-Info and Proxy-Authentication-Info header fields alone
   indicates that HTTP authentication is in use.  Additional information
   could be exposed by the contents of the authentication-scheme
   specific parameters; this will have to be considered in the
   definitions of these schemes.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
