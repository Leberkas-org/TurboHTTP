---
title: "8.5.  The CONNECT Method"
rfc_number: 9113
rfc_section: "8.5"
source_url: "https://www.rfc-editor.org/rfc/rfc9113"
description: "Section 8.5: The CONNECT Method — RFC 9113 — HTTP/2"
tags: [RFC9113, HTTP/2, binary-framing, streams, multiplexing, flow-control, SETTINGS, HPACK, GOAWAY, WINDOW_UPDATE, the_connect_method]
---

## 8.5.  The CONNECT Method

## 8.5  The CONNECT Method

   The CONNECT method (Section 9.3.6 of [HTTP]) is used to convert an
   HTTP connection into a tunnel to a remote host.  CONNECT is primarily
   used with HTTP proxies to establish a TLS session with an origin
   server for the purposes of interacting with "https" resources.

   In HTTP/2, the CONNECT method establishes a tunnel over a single
   HTTP/2 stream to a remote host, rather than converting the entire
   connection to a tunnel.  A CONNECT header section is constructed as
   defined in Section 8.3.1 ("Request Pseudo-Header Fields"), with a few
   differences.  Specifically:

   *  The ":method" pseudo-header field is set to CONNECT.

> **MUST**: *  The ":scheme" and ":path" pseudo-header fields MUST be omitted.

   *  The ":authority" pseudo-header field contains the host and port to
      connect to (equivalent to the authority-form of the request-target
      of CONNECT requests; see Section 3.2.3 of [HTTP/1.1]).

   A CONNECT request that does not conform to these restrictions is
   malformed (Section 8.1.1).

   A proxy that supports CONNECT establishes a TCP connection [TCP] to
   the host and port identified in the ":authority" pseudo-header field.
   Once this connection is successfully established, the proxy sends a
   HEADERS frame containing a 2xx-series status code to the client, as
   defined in Section 9.3.6 of [HTTP].

   After the initial HEADERS frame sent by each peer, all subsequent
   DATA frames correspond to data sent on the TCP connection.  The frame
   payload of any DATA frames sent by the client is transmitted by the
   proxy to the TCP server; data received from the TCP server is
   assembled into DATA frames by the proxy.  Frame types other than DATA
   or stream management frames (RST_STREAM, WINDOW_UPDATE, and PRIORITY)
> **MUST NOT**: MUST NOT be sent on a connected stream and MUST be treated as a
   stream error (Section 5.4.2) if received.

   The TCP connection can be closed by either peer.  The END_STREAM flag
   on a DATA frame is treated as being equivalent to the TCP FIN bit.  A
   client is expected to send a DATA frame with the END_STREAM flag set
   after receiving a frame with the END_STREAM flag set.  A proxy that
   receives a DATA frame with the END_STREAM flag set sends the attached
   data with the FIN bit set on the last TCP segment.  A proxy that
   receives a TCP segment with the FIN bit set sends a DATA frame with
   the END_STREAM flag set.  Note that the final TCP segment or DATA
   frame could be empty.

   A TCP connection error is signaled with RST_STREAM.  A proxy treats
   any error in the TCP connection, which includes receiving a TCP
   segment with the RST bit set, as a stream error (Section 5.4.2) of
> **MUST**: type CONNECT_ERROR.  Correspondingly, a proxy MUST send a TCP segment
   with the RST bit set if it detects an error with the stream or the
   HTTP/2 connection.

---

**Navigation:** [[../RFC9113|RFC9113 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
