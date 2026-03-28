---
title: "3.  Starting HTTP/2"
rfc_number: 9113
rfc_section: "3"
source_url: "https://www.rfc-editor.org/rfc/rfc9113"
description: "Section 3: Starting HTTP/2 — RFC 9113 — HTTP/2"
tags: [RFC9113, HTTP/2, binary-framing, streams, multiplexing, flow-control, SETTINGS, HPACK, GOAWAY, WINDOW_UPDATE, starting_http2]
---

## 3.  Starting HTTP/2

3.  Starting HTTP/2

   Implementations that generate HTTP requests need to discover whether
   a server supports HTTP/2.

   HTTP/2 uses the "http" and "https" URI schemes defined in Section 4.2
   of [HTTP], with the same default port numbers as HTTP/1.1 [HTTP/1.1].
   These URIs do not include any indication about what HTTP versions an
   upstream server (the immediate peer to which the client wishes to
   establish a connection) supports.

   The means by which support for HTTP/2 is determined is different for
   "http" and "https" URIs.  Discovery for "https" URIs is described in
   Section 3.2.  HTTP/2 support for "http" URIs can only be discovered
   by out-of-band means and requires prior knowledge of the support as
   described in Section 3.3.

## 3.1  HTTP/2 Version Identification

   The protocol defined in this document has two identifiers.  Creating
   a connection based on either implies the use of the transport,
   framing, and message semantics described in this document.

   *  The string "h2" identifies the protocol where HTTP/2 uses
      Transport Layer Security (TLS); see Section 9.2.  This identifier
      is used in the TLS Application-Layer Protocol Negotiation (ALPN)
      extension [TLS-ALPN] field and in any place where HTTP/2 over TLS
      is identified.

      The "h2" string is serialized into an ALPN protocol identifier as
      the two-octet sequence: 0x68, 0x32.

   *  The "h2c" string was previously used as a token for use in the
      HTTP Upgrade mechanism's Upgrade header field (Section 7.8 of
      [HTTP]).  This usage was never widely deployed and is deprecated
      by this document.  The same applies to the HTTP2-Settings header
      field, which was used with the upgrade to "h2c".

3.2.  Starting HTTP/2 for "https" URIs

   A client that makes a request to an "https" URI uses TLS [TLS13] with
   the ALPN extension [TLS-ALPN].

   HTTP/2 over TLS uses the "h2" protocol identifier.  The "h2c"
> **MUST NOT**: protocol identifier MUST NOT be sent by a client or selected by a
   server; the "h2c" protocol identifier describes a protocol that does
   not use TLS.

> **MUST**: Once TLS negotiation is complete, both the client and the server MUST
   send a connection preface (Section 3.4).

## 3.3  Starting HTTP/2 with Prior Knowledge

   A client can learn that a particular server supports HTTP/2 by other
   means.  For example, a client could be configured with knowledge that
   a server supports HTTP/2.

   A client that knows that a server supports HTTP/2 can establish a TCP
   connection and send the connection preface (Section 3.4) followed by
   HTTP/2 frames.  Servers can identify these connections by the
   presence of the connection preface.  This only affects the
   establishment of HTTP/2 connections over cleartext TCP; HTTP/2
> **MUST**: connections over TLS MUST use protocol negotiation in TLS [TLS-ALPN].

> **MUST**: Likewise, the server MUST send a connection preface (Section 3.4).

   Without additional information, prior support for HTTP/2 is not a
   strong signal that a given server will support HTTP/2 for future
   connections.  For example, it is possible for server configurations
   to change, for configurations to differ between instances in
   clustered servers, or for network conditions to change.

## 3.4  HTTP/2 Connection Preface

   In HTTP/2, each endpoint is required to send a connection preface as
   a final confirmation of the protocol in use and to establish the
   initial settings for the HTTP/2 connection.  The client and server
   each send a different connection preface.

   The client connection preface starts with a sequence of 24 octets,
   which in hex notation is:

     0x505249202a20485454502f322e300d0a0d0a534d0d0a0d0a

   That is, the connection preface starts with the string "PRI *
> **MUST**: HTTP/2.0\r\n\r\nSM\r\n\r\n".  This sequence MUST be followed by a
   SETTINGS frame (Section 6.5), which MAY be empty.  The client sends
   the client connection preface as the first application data octets of
   a connection.

      |  Note: The client connection preface is selected so that a large
      |  proportion of HTTP/1.1 or HTTP/1.0 servers and intermediaries
      |  do not attempt to process further frames.  Note that this does
      |  not address the concerns raised in [TALKING].

   The server connection preface consists of a potentially empty
> **MUST**: SETTINGS frame (Section 6.5) that MUST be the first frame the server
   sends in the HTTP/2 connection.

   The SETTINGS frames received from a peer as part of the connection
> **MUST**: preface MUST be acknowledged (see Section 6.5.3) after sending the
   connection preface.

   To avoid unnecessary latency, clients are permitted to send
   additional frames to the server immediately after sending the client
   connection preface, without waiting to receive the server connection
   preface.  It is important to note, however, that the server
   connection preface SETTINGS frame might include settings that
   necessarily alter how a client is expected to communicate with the
   server.  Upon receiving the SETTINGS frame, the client is expected to
   honor any settings established.  In some configurations, it is
   possible for the server to transmit SETTINGS before the client sends
   additional frames, providing an opportunity to avoid this issue.

> **MUST**: Clients and servers MUST treat an invalid connection preface as a
   connection error (Section 5.4.1) of type PROTOCOL_ERROR.  A GOAWAY
> **MAY**: frame (Section 6.8) MAY be omitted in this case, since an invalid
   preface indicates that the peer is not using HTTP/2.

---

**Navigation:** [[../RFC9113|RFC9113 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
