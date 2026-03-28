---
title: "8.7.  Request Reliability"
rfc_number: 9113
rfc_section: "8.7"
source_url: "https://www.rfc-editor.org/rfc/rfc9113"
description: "Section 8.7: Request Reliability — RFC 9113 — HTTP/2"
tags: [RFC9113, HTTP/2, binary-framing, streams, multiplexing, flow-control, SETTINGS, HPACK, GOAWAY, WINDOW_UPDATE, request_reliability]
---

## 8.7.  Request Reliability

## 8.7  Request Reliability

   In general, an HTTP client is unable to retry a non-idempotent
   request when an error occurs because there is no means to determine
   the nature of the error (see Section 9.2.2 of [HTTP]).  It is
   possible that some server processing occurred prior to the error,
   which could result in undesirable effects if the request were
   reattempted.

   HTTP/2 provides two mechanisms for providing a guarantee to a client
   that a request has not been processed:

   *  The GOAWAY frame indicates the highest stream number that might
      have been processed.  Requests on streams with higher numbers are
      therefore guaranteed to be safe to retry.

   *  The REFUSED_STREAM error code can be included in a RST_STREAM
      frame to indicate that the stream is being closed prior to any
      processing having occurred.  Any request that was sent on the
      reset stream can be safely retried.

> **MAY**: Requests that have not been processed have not failed; clients MAY
   automatically retry them, even those with non-idempotent methods.

> **MUST NOT**: A server MUST NOT indicate that a stream has not been processed
   unless it can guarantee that fact.  If frames that are on a stream
   are passed to the application layer for any stream, then
> **MUST NOT**: REFUSED_STREAM MUST NOT be used for that stream, and a GOAWAY frame
   MUST include a stream identifier that is greater than or equal to the
   given stream identifier.

   In addition to these mechanisms, the PING frame provides a way for a
   client to easily test a connection.  Connections that remain idle can
   become broken, because some middleboxes (for instance, network
   address translators or load balancers) silently discard connection
   bindings.  The PING frame allows a client to safely test whether a
   connection is still active without sending a request.

---

**Navigation:** [[../RFC9113|RFC9113 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
