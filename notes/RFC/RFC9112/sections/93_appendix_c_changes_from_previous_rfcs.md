---
title: "Appendix C.  Changes from Previous RFCs"
rfc_number: 9112
rfc_section: "Appendix C"
source_url: "https://www.rfc-editor.org/rfc/rfc9112"
description: "Appendix C: Changes from Previous RFCs — RFC 9112 — HTTP/1.1"
tags: [RFC9112, HTTP/1.1, message-framing, chunked-encoding, connection-management, keep-alive, Host-header, pipelining, changes_from_previous_rfcs]
---

## Appendix C.  Changes from Previous RFCs

Appendix C.  Changes from Previous RFCs

C.1.  Changes from HTTP/0.9

   Since HTTP/0.9 did not support header fields in a request, there is
   no mechanism for it to support name-based virtual hosts (selection of
   resource by inspection of the Host header field).  Any server that
   implements name-based virtual hosts ought to disable support for
   HTTP/0.9.  Most requests that appear to be HTTP/0.9 are, in fact,
   badly constructed HTTP/1.x requests caused by a client failing to
   properly encode the request-target.

C.2.  Changes from HTTP/1.0

C.2.1.  Multihomed Web Servers

   The requirements that clients and servers support the Host header
   field (Section 7.2 of [HTTP]), report an error if it is missing from
   an HTTP/1.1 request, and accept absolute URIs (Section 3.2) are among
   the most important changes defined by HTTP/1.1.

   Older HTTP/1.0 clients assumed a one-to-one relationship of IP
   addresses and servers; there was no established mechanism for
   distinguishing the intended server of a request other than the IP
   address to which that request was directed.  The Host header field
   was introduced during the development of HTTP/1.1 and, though it was
   quickly implemented by most HTTP/1.0 browsers, additional
   requirements were placed on all HTTP/1.1 requests in order to ensure
   complete adoption.  At the time of this writing, most HTTP-based
   services are dependent upon the Host header field for targeting
   requests.

C.2.2.  Keep-Alive Connections

   In HTTP/1.0, each connection is established by the client prior to
   the request and closed by the server after sending the response.
   However, some implementations implement the explicitly negotiated
   ("Keep-Alive") version of persistent connections described in
   Section 19.7.1 of [RFC2068].

   Some clients and servers might wish to be compatible with these
   previous approaches to persistent connections, by explicitly
   negotiating for them with a "Connection: keep-alive" request header
   field.  However, some experimental implementations of HTTP/1.0
   persistent connections are faulty; for example, if an HTTP/1.0 proxy
   server doesn't understand Connection, it will erroneously forward
   that header field to the next inbound server, which would result in a
   hung connection.

   One attempted solution was the introduction of a Proxy-Connection
   header field, targeted specifically at proxies.  In practice, this
   was also unworkable, because proxies are often deployed in multiple
   layers, bringing about the same problem discussed above.

   As a result, clients are encouraged not to send the Proxy-Connection
   header field in any requests.

   Clients are also encouraged to consider the use of "Connection: keep-
   alive" in requests carefully; while they can enable persistent
   connections with HTTP/1.0 servers, clients using them will need to
   monitor the connection for "hung" requests (which indicate that the
   client ought to stop sending the header field), and this mechanism
   ought not be used by clients at all when a proxy is being used.

C.2.3.  Introduction of Transfer-Encoding

   HTTP/1.1 introduces the Transfer-Encoding header field (Section 6.1).
   Transfer codings need to be decoded prior to forwarding an HTTP
   message over a MIME-compliant protocol.

C.3.  Changes from RFC 7230

   Most of the sections introducing HTTP's design goals, history,
   architecture, conformance criteria, protocol versioning, URIs,
   message routing, and header fields have been moved to [HTTP].  This
   document has been reduced to just the messaging syntax and connection
   management requirements specific to HTTP/1.1.

   Bare CRs have been prohibited outside of content.  (Section 2.2)

   The ABNF definition of authority-form has changed from the more
   general authority component of a URI (in which port is optional) to
   the specific host:port format that is required by CONNECT.
   (Section 3.2.3)

   Recipients are required to avoid smuggling/splitting attacks when
   processing an ambiguous message framing.  (Section 6.1)

   In the ABNF for chunked extensions, (bad) whitespace around ";" and
   "=" has been reintroduced.  Whitespace was removed in [RFC7230], but
   that change was found to break existing implementations.
   (Section 7.1.1)

   Trailer field semantics now transcend the specifics of chunked
   transfer coding.  The decoding algorithm for chunked (Section 7.1.3)
   has been updated to encourage storage/forwarding of trailer fields
   separately from the header section, to only allow merging into the
   header section if the recipient knows the corresponding field
   definition permits and defines how to merge, and otherwise to discard
   the trailer fields instead of merging.  The trailer part is now
   called the trailer section to be more consistent with the header
   section and more distinct from a body part.  (Section 7.1.2)

   Transfer coding parameters called "q" are disallowed in order to
   avoid conflicts with the use of ranks in the TE header field.
   (Section 7.3)

---

**Navigation:** [[../RFC9112|RFC9112 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
