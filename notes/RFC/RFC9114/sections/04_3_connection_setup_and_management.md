---
title: "3.  Connection Setup and Management"
rfc_number: 9114
rfc_section: "3"
source_url: "https://www.rfc-editor.org/rfc/rfc9114"
description: "Section 3: Connection Setup and Management — RFC 9114 — HTTP/3"
tags: [RFC9114, HTTP/3, QUIC, variable-length-frames, unidirectional-streams, QPACK, SETTINGS, GOAWAY, server-push, connection_setup_and_management]
---

## 3.  Connection Setup and Management

3.  Connection Setup and Management

## 3.1  Discovering an HTTP/3 Endpoint

   HTTP relies on the notion of an authoritative response: a response
   that has been determined to be the most appropriate response for that
   request given the state of the target resource at the time of
   response message origination by (or at the direction of) the origin
   server identified within the target URI.  Locating an authoritative
   server for an HTTP URI is discussed in Section 4.3 of [HTTP].

   The "https" scheme associates authority with possession of a
   certificate that the client considers to be trustworthy for the host
   identified by the authority component of the URI.  Upon receiving a
> **MUST**: server certificate in the TLS handshake, the client MUST verify that
   the certificate is an acceptable match for the URI's origin server
   using the process described in Section 4.3.4 of [HTTP].  If the
   certificate cannot be verified with respect to the URI's origin
> **MUST NOT**: server, the client MUST NOT consider the server authoritative for
   that origin.

> **MAY**: A client MAY attempt access to a resource with an "https" URI by
   resolving the host identifier to an IP address, establishing a QUIC
   connection to that address on the indicated port (including
   validation of the server certificate as described above), and sending
   an HTTP/3 request message targeting the URI to the server over that
   secured connection.  Unless some other mechanism is used to select
   HTTP/3, the token "h3" is used in the Application-Layer Protocol
   Negotiation (ALPN; see [RFC7301]) extension during the TLS handshake.

   Connectivity problems (e.g., blocking UDP) can result in a failure to
> **SHOULD**: establish a QUIC connection; clients SHOULD attempt to use TCP-based
   versions of HTTP in this case.

> **MAY**: Servers MAY serve HTTP/3 on any UDP port; an alternative service
   advertisement always includes an explicit port, and URIs contain
   either an explicit port or a default port associated with the scheme.

### 3.1.1  HTTP Alternative Services

   An HTTP origin can advertise the availability of an equivalent HTTP/3
   endpoint via the Alt-Svc HTTP response header field or the HTTP/2
   ALTSVC frame ([ALTSVC]) using the "h3" ALPN token.

   For example, an origin could indicate in an HTTP response that HTTP/3
   was available on UDP port 50781 at the same hostname by including the
   following header field:

   Alt-Svc: h3=":50781"

   On receipt of an Alt-Svc record indicating HTTP/3 support, a client
> **MAY**: MAY attempt to establish a QUIC connection to the indicated host and
   port; if this connection is successful, the client can send HTTP
   requests using the mapping described in this document.

### 3.1.2  Other Schemes

   Although HTTP is independent of the transport protocol, the "http"
   scheme associates authority with the ability to receive TCP
   connections on the indicated port of whatever host is identified
   within the authority component.  Because HTTP/3 does not use TCP,
   HTTP/3 cannot be used for direct access to the authoritative server
   for a resource identified by an "http" URI.  However, protocol
   extensions such as [ALTSVC] permit the authoritative server to
   identify other services that are also authoritative and that might be
   reachable over HTTP/3.

   Prior to making requests for an origin whose scheme is not "https",
> **MUST**: the client MUST ensure the server is willing to serve that scheme.
   For origins whose scheme is "http", an experimental method to
   accomplish this is described in [RFC8164].  Other mechanisms might be
   defined for various schemes in the future.

## 3.2  Connection Establishment

   HTTP/3 relies on QUIC version 1 as the underlying transport.  The use
> **MAY**: of other QUIC transport versions with HTTP/3 MAY be defined by future
   specifications.

   QUIC version 1 uses TLS version 1.3 or greater as its handshake
> **MUST**: protocol.  HTTP/3 clients MUST support a mechanism to indicate the
   target host to the server during the TLS handshake.  If the server is
> **MUST**: identified by a domain name ([DNS-TERMS]), clients MUST send the
   Server Name Indication (SNI; [RFC6066]) TLS extension unless an
   alternative mechanism to indicate the target host is used.

   QUIC connections are established as described in [QUIC-TRANSPORT].
   During connection establishment, HTTP/3 support is indicated by
   selecting the ALPN token "h3" in the TLS handshake.  Support for
> **MAY**: other application-layer protocols MAY be offered in the same
   handshake.

   While connection-level options pertaining to the core QUIC protocol
   are set in the initial crypto handshake, settings specific to HTTP/3
   are conveyed in the SETTINGS frame.  After the QUIC connection is
> **MUST**: established, a SETTINGS frame MUST be sent by each endpoint as the
   initial frame of their respective HTTP control stream.

## 3.3  Connection Reuse

   HTTP/3 connections are persistent across multiple requests.  For best
   performance, it is expected that clients will not close connections
   until it is determined that no further communication with a server is
   necessary (for example, when a user navigates away from a particular
   web page) or until the server closes the connection.

> **MAY**: Once a connection to a server endpoint exists, this connection MAY be
   reused for requests with multiple different URI authority components.
> **MUST**: To use an existing connection for a new origin, clients MUST validate
   the certificate presented by the server for the new origin server
   using the process described in Section 4.3.4 of [HTTP].  This implies
   that clients will need to retain the server certificate and any
   additional information needed to verify that certificate; clients
   that do not do so will be unable to reuse the connection for
   additional origins.

   If the certificate is not acceptable with regard to the new origin
> **MUST NOT**: for any reason, the connection MUST NOT be reused and a new
   connection SHOULD be established for the new origin.  If the reason
   the certificate cannot be verified might apply to other origins
> **SHOULD**: already associated with the connection, the client SHOULD revalidate
   the server certificate for those origins.  For instance, if
   validation of a certificate fails because the certificate has expired
   or been revoked, this might be used to invalidate all other origins
   for which that certificate was used to establish authority.

> **SHOULD NOT**: Clients SHOULD NOT open more than one HTTP/3 connection to a given IP
   address and UDP port, where the IP address and port might be derived
   from a URI, a selected alternative service ([ALTSVC]), a configured
> **MAY**: proxy, or name resolution of any of these.  A client MAY open
   multiple HTTP/3 connections to the same IP address and UDP port using
> **SHOULD**: different transport or TLS configurations but SHOULD avoid creating
   multiple connections with the same configuration.

   Servers are encouraged to maintain open HTTP/3 connections for as
   long as possible but are permitted to terminate idle connections if
   necessary.  When either endpoint chooses to close the HTTP/3
> **SHOULD**: connection, the terminating endpoint SHOULD first send a GOAWAY frame
   (Section 5.2) so that both endpoints can reliably determine whether
   previously sent frames have been processed and gracefully complete or
   terminate any necessary remaining tasks.

   A server that does not wish clients to reuse HTTP/3 connections for a
   particular origin can indicate that it is not authoritative for a
   request by sending a 421 (Misdirected Request) status code in
   response to the request; see Section 7.4 of [HTTP].

---

## TurboHttp Compliance

**Status**: ⚠️ Partial

### Implementation Notes

- **`Http3ControlStream.cs`** — Manages the HTTP/3 control stream lifecycle with state machine (`AwaitingSettings` → `Active` → `GoAway` → `Closed`); sends SETTINGS as first frame per §3.2
- **`Http3Settings.cs`** — Encodes/decodes SETTINGS parameters using QUIC variable-length integers; supports `SETTINGS_MAX_FIELD_SECTION_SIZE` and reserved identifiers per §7.2.4.1
- **`Http3Connection.cs`** — Connection lifecycle management including GOAWAY frame exchange for graceful shutdown per §3.3
- **`QuicTransportAdapter.cs`** — QUIC transport abstraction bridging System.Net.Quic to TurboHttp's connection model

### Test References

- `TurboHttp.StreamTests/` — ~134 stream-level tests covering control stream state transitions and connection setup
- `TurboHttp.Tests/RFC9114/` — 32 unit test files covering frame encoding, settings validation, error codes

### Known Gaps

- ❌ Alt-Svc discovery (§3.1.1) not implemented — connections use direct QUIC endpoints only
- ❌ Connection reuse certificate validation (§3.3) not implemented — each origin gets a dedicated connection
- ❌ 0-RTT QUIC resumption with stored SETTINGS (§7.2.4.2) not supported
- ⚠️ Server push streams (§6.2.2) not implemented — client-only library does not need to send push, but should reject server-initiated push gracefully

---

**Navigation:** [[../RFC9114|RFC9114 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
