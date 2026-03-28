---
title: "4.3.  Authoritative Access"
rfc_number: 9110
rfc_section: "4.3"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 4.3: Authoritative Access — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, authoritative_access]
---

## 4.3.  Authoritative Access

## 4.3  Authoritative Access

   Authoritative access refers to dereferencing a given identifier, for
   the sake of access to the identified resource, in a way that the
   client believes is authoritative (controlled by the resource owner).
   The process for determining whether access is granted is defined by
   the URI scheme and often uses data within the URI components, such as
   the authority component when the generic syntax is used.  However,
   authoritative access is not limited to the identified mechanism.

   Section 4.3.1 defines the concept of an origin as an aid to such
   uses, and the subsequent subsections explain how to establish that a
   peer has the authority to represent an origin.

   See Section 17.1 for security considerations related to establishing
   authority.

### 4.3.1  URI Origin

   The "origin" for a given URI is the triple of scheme, host, and port
   after normalizing the scheme and host to lowercase and normalizing
   the port to remove any leading zeros.  If port is elided from the
   URI, the default port for that scheme is used.  For example, the URI

      https://Example.Com/happy.js

   would have the origin

      { "https", "example.com", "443" }

   which can also be described as the normalized URI prefix with port
   always present:

      https://example.com:443

   Each origin defines its own namespace and controls how identifiers
   within that namespace are mapped to resources.  In turn, how the
   origin responds to valid requests, consistently over time, determines
   the semantics that users will associate with a URI, and the
   usefulness of those semantics is what ultimately transforms these
   mechanisms into a resource for users to reference and access in the
   future.

   Two origins are distinct if they differ in scheme, host, or port.
   Even when it can be verified that the same entity controls two
   distinct origins, the two namespaces under those origins are distinct
   unless explicitly aliased by a server authoritative for that origin.

   Origin is also used within HTML and related Web protocols, beyond the
   scope of this document, as described in [RFC6454].

4.3.2.  http Origins

   Although HTTP is independent of the transport protocol, the "http"
   scheme (Section 4.2.1) is specific to associating authority with
   whomever controls the origin server listening for TCP connections on
   the indicated port of whatever host is identified within the
   authority component.  This is a very weak sense of authority because
   it depends on both client-specific name resolution mechanisms and
   communication that might not be secured from an on-path attacker.
   Nevertheless, it is a sufficient minimum for binding "http"
   identifiers to an origin server for consistent resolution within a
   trusted environment.

   If the host identifier is provided as an IP address, the origin
   server is the listener (if any) on the indicated TCP port at that IP
   address.  If host is a registered name, the registered name is an
   indirect identifier for use with a name resolution service, such as
   DNS, to find an address for an appropriate origin server.

   When an "http" URI is used within a context that calls for access to
> **MAY**: the indicated resource, a client MAY attempt access by resolving the
   host identifier to an IP address, establishing a TCP connection to
   that address on the indicated port, and sending over that connection
   an HTTP request message containing a request target that matches the
   client's target URI (Section 7.1).

   If the server responds to such a request with a non-interim HTTP
   response message, as described in Section 15, then that response is
   considered an authoritative answer to the client's request.

   Note, however, that the above is not the only means for obtaining an
   authoritative response, nor does it imply that an authoritative
   response is always necessary (see [CACHING]).  For example, the Alt-
   Svc header field [ALTSVC] allows an origin server to identify other
   services that are also authoritative for that origin.  Access to
   "http" identified resources might also be provided by protocols
   outside the scope of this document.

4.3.3.  https Origins

   The "https" scheme (Section 4.2.2) associates authority based on the
   ability of a server to use the private key corresponding to a
   certificate that the client considers to be trustworthy for the
   identified origin server.  The client usually relies upon a chain of
   trust, conveyed from some prearranged or configured trust anchor, to
   deem a certificate trustworthy (Section 4.3.4).

   In HTTP/1.1 and earlier, a client will only attribute authority to a
   server when they are communicating over a successfully established
   and secured connection specifically to that URI origin's host.  The
   connection establishment and certificate verification are used as
   proof of authority.

   In HTTP/2 and HTTP/3, a client will attribute authority to a server
   when they are communicating over a successfully established and
   secured connection if the URI origin's host matches any of the hosts
   present in the server's certificate and the client believes that it
   could open a connection to that host for that URI.  In practice, a
   client will make a DNS query to check that the origin's host contains
   the same server IP address as the established connection.  This
   restriction can be removed by the origin server sending an equivalent
   ORIGIN frame [RFC8336].

   The request target's host and port value are passed within each HTTP
   request, identifying the origin and distinguishing it from other
   namespaces that might be controlled by the same server (Section 7.2).
   It is the origin's responsibility to ensure that any services
   provided with control over its certificate's private key are equally
   responsible for managing the corresponding "https" namespaces or at
   least prepared to reject requests that appear to have been
   misdirected (Section 7.4).

   An origin server might be unwilling to process requests for certain
   target URIs even when they have the authority to do so.  For example,
   when a host operates distinct services on different ports (e.g., 443
   and 8000), checking the target URI at the origin server is necessary
   (even after the connection has been secured) because a network
   attacker might cause connections for one port to be received at some
   other port.  Failing to check the target URI might allow such an
   attacker to replace a response to one target URI (e.g.,
   "https://example.com/foo") with a seemingly authoritative response
   from the other port (e.g., "https://example.com:8000/foo").

   Note that the "https" scheme does not rely on TCP and the connected
   port number for associating authority, since both are outside the
   secured communication and thus cannot be trusted as definitive.
   Hence, the HTTP communication might take place over any channel that
   has been secured, as defined in Section 4.2.2, including protocols
   that don't use TCP.

   When an "https" URI is used within a context that calls for access to
> **MAY**: the indicated resource, a client MAY attempt access by resolving the
   host identifier to an IP address, establishing a TCP connection to
   that address on the indicated port, securing the connection end-to-
   end by successfully initiating TLS over TCP with confidentiality and
   integrity protection, and sending over that connection an HTTP
   request message containing a request target that matches the client's
   target URI (Section 7.1).

   If the server responds to such a request with a non-interim HTTP
   response message, as described in Section 15, then that response is
   considered an authoritative answer to the client's request.

   Note, however, that the above is not the only means for obtaining an
   authoritative response, nor does it imply that an authoritative
   response is always necessary (see [CACHING]).

4.3.4.  https Certificate Verification

> **MUST**: To establish a secured connection to dereference a URI, a client MUST
   verify that the service's identity is an acceptable match for the
   URI's origin server.  Certificate verification is used to prevent
   server impersonation by an on-path attacker or by an attacker that
   controls name resolution.  This process requires that a client be
   configured with a set of trust anchors.

> **MUST**: In general, a client MUST verify the service identity using the
   verification process defined in Section 6 of [RFC6125].  The client
> **MUST**: MUST construct a reference identity from the service's host: if the
   host is a literal IP address (Section 4.3.5), the reference identity
   is an IP-ID, otherwise the host is a name and the reference identity
   is a DNS-ID.

> **MUST NOT**: A reference identity of type CN-ID MUST NOT be used by clients.  As
   noted in Section 6.2.1 of [RFC6125], a reference identity of type CN-
   ID might be used by older clients.

   A client might be specially configured to accept an alternative form
   of server identity verification.  For example, a client might be
   connecting to a server whose address and hostname are dynamic, with
   an expectation that the service will present a specific certificate
   (or a certificate matching some externally defined reference
   identity) rather than one matching the target URI's origin.

   In special cases, it might be appropriate for a client to simply
   ignore the server's identity, but it must be understood that this
   leaves a connection open to active attack.

   If the certificate is not valid for the target URI's origin, a user
> **MUST**: agent MUST either obtain confirmation from the user before proceeding
   (see Section 3.5) or terminate the connection with a bad certificate
> **MUST**: error.  Automated clients MUST log the error to an appropriate audit
   log (if available) and SHOULD terminate the connection (with a bad
> **MAY**: certificate error).  Automated clients MAY provide a configuration
   setting that disables this check, but MUST provide a setting which
   enables it.

### 4.3.5  IP-ID Reference Identity

   A server that is identified using an IP address literal in the "host"
   field of an "https" URI has a reference identity of type IP-ID.  An
   IP version 4 address uses the "IPv4address" ABNF rule, and an IP
   version 6 address uses the "IP-literal" production with the
   "IPv6address" option; see Section 3.2.2 of [URI].  A reference
   identity of IP-ID contains the decoded bytes of the IP address.

   An IP version 4 address is 4 octets, and an IP version 6 address is
   16 octets.  Use of IP-ID is not defined for any other IP version.
   The iPAddress choice in the certificate subjectAltName extension does
   not explicitly include the IP version and so relies on the length of
   the address to distinguish versions; see Section 4.2.1.6 of
   [RFC5280].

   A reference identity of type IP-ID matches if the address is
   identical to an iPAddress value of the subjectAltName extension of
   the certificate.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
