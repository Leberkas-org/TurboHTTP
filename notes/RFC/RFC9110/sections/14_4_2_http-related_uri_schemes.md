---
title: "4.2.  HTTP-Related URI Schemes"
rfc_number: 9110
rfc_section: "4.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 4.2: HTTP-Related URI Schemes — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, http-related_uri_schemes]
---

## 4.2.  HTTP-Related URI Schemes

## 4.2  HTTP-Related URI Schemes

   IANA maintains the registry of URI Schemes [BCP35] at
   <https://www.iana.org/assignments/uri-schemes/>.  Although requests
   might target any URI scheme, the following schemes are inherent to
   HTTP servers:

   +============+====================================+=========+
   | URI Scheme | Description                        | Section |
   +============+====================================+=========+
   | http       | Hypertext Transfer Protocol        | 4.2.1   |
   +------------+------------------------------------+---------+
   | https      | Hypertext Transfer Protocol Secure | 4.2.2   |
   +------------+------------------------------------+---------+

                              Table 2

   Note that the presence of an "http" or "https" URI does not imply
   that there is always an HTTP server at the identified origin
   listening for connections.  Anyone can mint a URI, whether or not a
   server exists and whether or not that server currently maps that
   identifier to a resource.  The delegated nature of registered names
   and IP addresses creates a federated namespace whether or not an HTTP
   server is present.

4.2.1.  http URI Scheme

   The "http" URI scheme is hereby defined for minting identifiers
   within the hierarchical namespace governed by a potential HTTP origin
   server listening for TCP ([TCP]) connections on a given port.


```abnf
     http-URI = "http" "://" authority path-abempty [ "?" query ]
```


   The origin server for an "http" URI is identified by the authority
   component, which includes a host identifier ([URI], Section 3.2.2)
   and optional port number ([URI], Section 3.2.3).  If the port
   subcomponent is empty or not given, TCP port 80 (the reserved port
   for WWW services) is the default.  The origin determines who has the
   right to respond authoritatively to requests that target the
   identified resource, as defined in Section 4.3.2.

> **MUST NOT**: A sender MUST NOT generate an "http" URI with an empty host
   identifier.  A recipient that processes such a URI reference MUST
   reject it as invalid.

   The hierarchical path component and optional query component identify
   the target resource within that origin server's namespace.

4.2.2.  https URI Scheme

   The "https" URI scheme is hereby defined for minting identifiers
   within the hierarchical namespace governed by a potential origin
   server listening for TCP connections on a given port and capable of
   establishing a TLS ([TLS13]) connection that has been secured for
   HTTP communication.  In this context, "secured" specifically means
   that the server has been authenticated as acting on behalf of the
   identified authority and all HTTP communication with that server has
   confidentiality and integrity protection that is acceptable to both
   client and server.


```abnf
     https-URI = "https" "://" authority path-abempty [ "?" query ]
```


   The origin server for an "https" URI is identified by the authority
   component, which includes a host identifier ([URI], Section 3.2.2)
   and optional port number ([URI], Section 3.2.3).  If the port
   subcomponent is empty or not given, TCP port 443 (the reserved port
   for HTTP over TLS) is the default.  The origin determines who has the
   right to respond authoritatively to requests that target the
   identified resource, as defined in Section 4.3.3.

> **MUST NOT**: A sender MUST NOT generate an "https" URI with an empty host
   identifier.  A recipient that processes such a URI reference MUST
   reject it as invalid.

   The hierarchical path component and optional query component identify
   the target resource within that origin server's namespace.

> **MUST**: A client MUST ensure that its HTTP requests for an "https" resource
   are secured, prior to being communicated, and that it only accepts
   secured responses to those requests.  Note that the definition of
   what cryptographic mechanisms are acceptable to client and server are
   usually negotiated and can change over time.

   Resources made available via the "https" scheme have no shared
   identity with the "http" scheme.  They are distinct origins with
   separate namespaces.  However, extensions to HTTP that are defined as
   applying to all origins with the same host, such as the Cookie
   protocol [COOKIE], allow information set by one service to impact
   communication with other services within a matching group of host
   domains.  Such extensions ought to be designed with great care to
   prevent information obtained from a secured connection being
   inadvertently exchanged within an unsecured context.

4.2.3.  http(s) Normalization and Comparison

   URIs with an "http" or "https" scheme are normalized and compared
   according to the methods defined in Section 6 of [URI], using the
   defaults described above for each scheme.

   HTTP does not require the use of a specific method for determining
   equivalence.  For example, a cache key might be compared as a simple
   string, after syntax-based normalization, or after scheme-based
   normalization.

   Scheme-based normalization (Section 6.2.3 of [URI]) of "http" and
   "https" URIs involves the following additional rules:

   *  If the port is equal to the default port for a scheme, the normal
      form is to omit the port subcomponent.

   *  When not being used as the target of an OPTIONS request, an empty
      path component is equivalent to an absolute path of "/", so the
      normal form is to provide a path of "/" instead.

   *  The scheme and host are case-insensitive and normally provided in
      lowercase; all other components are compared in a case-sensitive
      manner.

   *  Characters other than those in the "reserved" set are equivalent
      to their percent-encoded octets: the normal form is to not encode
      them (see Sections 2.1 and 2.2 of [URI]).

   For example, the following three URIs are equivalent:

      http://example.com:80/~smith/home.html
      http://EXAMPLE.com/%7Esmith/home.html
      http://EXAMPLE.com:/%7esmith/home.html

   Two HTTP URIs that are equivalent after normalization (using any
   method) can be assumed to identify the same resource, and any HTTP
> **MAY**: component MAY perform normalization.  As a result, distinct resources
   SHOULD NOT be identified by HTTP URIs that are equivalent after
   normalization (using any method defined in Section 6.2 of [URI]).

### 4.2.4  Deprecation of userinfo in http(s) URIs

   The URI generic syntax for authority also includes a userinfo
   subcomponent ([URI], Section 3.2.1) for including user authentication
   information in the URI.  In that subcomponent, the use of the format
   "user:password" is deprecated.

   Some implementations make use of the userinfo component for internal
   configuration of authentication information, such as within command
   invocation options, configuration files, or bookmark lists, even
   though such usage might expose a user identifier or password.

> **MUST NOT**: A sender MUST NOT generate the userinfo subcomponent (and its "@"
   delimiter) when an "http" or "https" URI reference is generated
   within a message as a target URI or field value.

   Before making use of an "http" or "https" URI reference received from
> **SHOULD**: an untrusted source, a recipient SHOULD parse for userinfo and treat
   its presence as an error; it is likely being used to obscure the
   authority for the sake of phishing attacks.

4.2.5.  http(s) References with Fragment Identifiers

   Fragment identifiers allow for indirect identification of a secondary
   resource, independent of the URI scheme, as defined in Section 3.5 of
   [URI].  Some protocol elements that refer to a URI allow inclusion of
   a fragment, while others do not.  They are distinguished by use of
   the ABNF rule for elements where fragment is allowed; otherwise, a
   specific rule that excludes fragments is used.

      |  *Note:* The fragment identifier component is not part of the
      |  scheme definition for a URI scheme (see Section 4.3 of [URI]),
      |  thus does not appear in the ABNF definitions for the "http" and
      |  "https" URI schemes above.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
