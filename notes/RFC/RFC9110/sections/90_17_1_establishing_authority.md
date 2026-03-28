---
title: "17.1.  Establishing Authority"
rfc_number: 9110
rfc_section: "17.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 17.1: Establishing Authority — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, establishing_authority]
---

## 17.1.  Establishing Authority

17.  Security Considerations

   This section is meant to inform developers, information providers,
   and users of known security concerns relevant to HTTP semantics and
   its use for transferring information over the Internet.
   Considerations related to caching are discussed in Section 7 of
   [CACHING], and considerations related to HTTP/1.1 message syntax and
   parsing are discussed in Section 11 of [HTTP/1.1].

   The list of considerations below is not exhaustive.  Most security
   concerns related to HTTP semantics are about securing server-side
   applications (code behind the HTTP interface), securing user agent
   processing of content received via HTTP, or secure use of the
   Internet in general, rather than security of the protocol.  The
   security considerations for URIs, which are fundamental to HTTP
   operation, are discussed in Section 7 of [URI].  Various
   organizations maintain topical information and links to current
   research on Web application security (e.g., [OWASP]).

## 17.1  Establishing Authority

   HTTP relies on the notion of an "authoritative response": a response
   that has been determined by (or at the direction of) the origin
   server identified within the target URI to be the most appropriate
   response for that request given the state of the target resource at
   the time of response message origination.

   When a registered name is used in the authority component, the "http"
   URI scheme (Section 4.2.1) relies on the user's local name resolution
   service to determine where it can find authoritative responses.  This
   means that any attack on a user's network host table, cached names,
   or name resolution libraries becomes an avenue for attack on
   establishing authority for "http" URIs.  Likewise, the user's choice
   of server for Domain Name Service (DNS), and the hierarchy of servers
   from which it obtains resolution results, could impact the
   authenticity of address mappings; DNS Security Extensions (DNSSEC,
   [RFC4033]) are one way to improve authenticity, as are the various
   mechanisms for making DNS requests over more secure transfer
   protocols.

   Furthermore, after an IP address is obtained, establishing authority
   for an "http" URI is vulnerable to attacks on Internet Protocol
   routing.

   The "https" scheme (Section 4.2.2) is intended to prevent (or at
   least reveal) many of these potential attacks on establishing
   authority, provided that the negotiated connection is secured and the
   client properly verifies that the communicating server's identity
   matches the target URI's authority component (Section 4.3.4).
   Correctly implementing such verification can be difficult (see
   [Georgiev]).

   Authority for a given origin server can be delegated through protocol
   extensions; for example, [ALTSVC].  Likewise, the set of servers for
   which a connection is considered authoritative can be changed with a
   protocol extension like [RFC8336].

   Providing a response from a non-authoritative source, such as a
   shared proxy cache, is often useful to improve performance and
   availability, but only to the extent that the source can be trusted
   or the distrusted response can be safely used.

   Unfortunately, communicating authority to users can be difficult.
   For example, "phishing" is an attack on the user's perception of
   authority, where that perception can be misled by presenting similar
   branding in hypertext, possibly aided by userinfo obfuscating the
   authority component (see Section 4.2.1).  User agents can reduce the
   impact of phishing attacks by enabling users to easily inspect a
   target URI prior to making an action, by prominently distinguishing
   (or rejecting) userinfo when present, and by not sending stored
   credentials and cookies when the referring document is from an
   unknown or untrusted source.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
