---
title: "5.4.  Pragma"
rfc_number: 9111
rfc_section: "5.4"
source_url: "https://www.rfc-editor.org/rfc/rfc9111"
description: "Section 5.4: Pragma — RFC 9111 — HTTP Caching"
tags: [RFC9111, HTTP-caching, freshness, validation, Cache-Control, max-age, Expires, conditional-requests, Vary, pragma]
---

## 5.4.  Pragma

## 5.4  Pragma

   The "Pragma" request header field was defined for HTTP/1.0 caches, so
   that clients could specify a "no-cache" request (as Cache-Control was
   not defined until HTTP/1.1).

   However, support for Cache-Control is now widespread.  As a result,
   this specification deprecates Pragma.

      |  *Note:* Because the meaning of "Pragma: no-cache" in responses
      |  was never specified, it does not provide a reliable replacement
      |  for "Cache-Control: no-cache" in them.

---

**Navigation:** [[../RFC9111|RFC9111 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
