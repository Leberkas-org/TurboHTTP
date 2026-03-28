---
title: "14.3.  Accept-Ranges"
rfc_number: 9110
rfc_section: "14.3"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 14.3: Accept-Ranges — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, accept-ranges]
---

## 14.3.  Accept-Ranges

## 14.3  Accept-Ranges

   The "Accept-Ranges" field in a response indicates whether an upstream
   server supports range requests for the target resource.


```abnf
     Accept-Ranges     = acceptable-ranges
     acceptable-ranges = 1#range-unit
```


   For example, a server that supports byte-range requests
   (Section 14.1.2) can send the field

   Accept-Ranges: bytes

   to indicate that it supports byte range requests for that target
   resource, thereby encouraging its use by the client for future
   partial requests on the same request path.  Range units are defined
   in Section 14.1.

> **MAY**: A client MAY generate range requests regardless of having received an
   Accept-Ranges field.  The information only provides advice for the
   sake of improving performance and reducing unnecessary network
   transfers.

> **MUST NOT**: Conversely, a client MUST NOT assume that receiving an Accept-Ranges
   field means that future range requests will return partial responses.
   The content might change, the server might only support range
   requests at certain times or under certain conditions, or a different
   intermediary might process the next request.

   A server that does not support any kind of range request for the
> **MAY**: target resource MAY send

   Accept-Ranges: none

   to advise the client not to attempt a range request on the same
   request path.  The range unit "none" is reserved for this purpose.

> **MAY**: The Accept-Ranges field MAY be sent in a trailer section, but is
   preferred to be sent as a header field because the information is
   particularly useful for restarting large information transfers that
   have failed in mid-content (before the trailer section is received).

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
