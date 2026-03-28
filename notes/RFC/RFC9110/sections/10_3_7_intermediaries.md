---
title: "3.7.  Intermediaries"
rfc_number: 9110
rfc_section: "3.7"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 3.7: Intermediaries — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, intermediaries]
---

## 3.7.  Intermediaries

## 3.7  Intermediaries

   HTTP enables the use of intermediaries to satisfy requests through a
   chain of connections.  There are three common forms of HTTP
   "intermediary": proxy, gateway, and tunnel.  In some cases, a single
   intermediary might act as an origin server, proxy, gateway, or
   tunnel, switching behavior based on the nature of each request.

            >             >             >             >
       UA =========== A =========== B =========== C =========== O
                  <             <             <             <

                                  Figure 2

   The figure above shows three intermediaries (A, B, and C) between the
   user agent and origin server.  A request or response message that
   travels the whole chain will pass through four separate connections.
   Some HTTP communication options might apply only to the connection
   with the nearest, non-tunnel neighbor, only to the endpoints of the
   chain, or to all connections along the chain.  Although the diagram
   is linear, each participant might be engaged in multiple,
   simultaneous communications.  For example, B might be receiving
   requests from many clients other than A, and/or forwarding requests
   to servers other than C, at the same time that it is handling A's
   request.  Likewise, later requests might be sent through a different
   path of connections, often based on dynamic configuration for load
   balancing.

   The terms "upstream" and "downstream" are used to describe
   directional requirements in relation to the message flow: all
   messages flow from upstream to downstream.  The terms "inbound" and
   "outbound" are used to describe directional requirements in relation
   to the request route: inbound means "toward the origin server",
   whereas outbound means "toward the user agent".

   A "proxy" is a message-forwarding agent that is chosen by the client,
   usually via local configuration rules, to receive requests for some
   type(s) of absolute URI and attempt to satisfy those requests via
   translation through the HTTP interface.  Some translations are
   minimal, such as for proxy requests for "http" URIs, whereas other
   requests might require translation to and from entirely different
   application-level protocols.  Proxies are often used to group an
   organization's HTTP requests through a common intermediary for the
   sake of security services, annotation services, or shared caching.
   Some proxies are designed to apply transformations to selected
   messages or content while they are being forwarded, as described in
   Section 7.7.

   A "gateway" (a.k.a. "reverse proxy") is an intermediary that acts as
   an origin server for the outbound connection but translates received
   requests and forwards them inbound to another server or servers.
   Gateways are often used to encapsulate legacy or untrusted
   information services, to improve server performance through
   "accelerator" caching, and to enable partitioning or load balancing
   of HTTP services across multiple machines.

   All HTTP requirements applicable to an origin server also apply to
   the outbound communication of a gateway.  A gateway communicates with
   inbound servers using any protocol that it desires, including private
   extensions to HTTP that are outside the scope of this specification.
   However, an HTTP-to-HTTP gateway that wishes to interoperate with
   third-party HTTP servers needs to conform to user agent requirements
   on the gateway's inbound connection.

   A "tunnel" acts as a blind relay between two connections without
   changing the messages.  Once active, a tunnel is not considered a
   party to the HTTP communication, though the tunnel might have been
   initiated by an HTTP request.  A tunnel ceases to exist when both
   ends of the relayed connection are closed.  Tunnels are used to
   extend a virtual connection through an intermediary, such as when
   Transport Layer Security (TLS, [TLS13]) is used to establish
   confidential communication through a shared firewall proxy.

   The above categories for intermediary only consider those acting as
   participants in the HTTP communication.  There are also
   intermediaries that can act on lower layers of the network protocol
   stack, filtering or redirecting HTTP traffic without the knowledge or
   permission of message senders.  Network intermediaries are
   indistinguishable (at a protocol level) from an on-path attacker,
   often introducing security flaws or interoperability problems due to
   mistakenly violating HTTP semantics.

   For example, an "interception proxy" [RFC3040] (also commonly known
   as a "transparent proxy" [RFC1919]) differs from an HTTP proxy
   because it is not chosen by the client.  Instead, an interception
   proxy filters or redirects outgoing TCP port 80 packets (and
   occasionally other common port traffic).  Interception proxies are
   commonly found on public network access points, as a means of
   enforcing account subscription prior to allowing use of non-local
   Internet services, and within corporate firewalls to enforce network
   usage policies.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
