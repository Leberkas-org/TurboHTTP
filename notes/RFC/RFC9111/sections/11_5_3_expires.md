---
title: "5.3.  Expires"
rfc_number: 9111
rfc_section: "5.3"
source_url: "https://www.rfc-editor.org/rfc/rfc9111"
description: "Section 5.3: Expires — RFC 9111 — HTTP Caching"
tags: [RFC9111, HTTP-caching, freshness, validation, Cache-Control, max-age, Expires, conditional-requests, Vary]
---

## 5.3.  Expires

## 5.3  Expires

   The "Expires" response header field gives the date/time after which
   the response is considered stale.  See Section 4.2 for further
   discussion of the freshness model.

   The presence of an Expires header field does not imply that the
   original resource will change or cease to exist at, before, or after
   that time.

   The Expires field value is an HTTP-date timestamp, as defined in
   Section 5.6.7 of [HTTP].  See also Section 4.2 for parsing
   requirements specific to caches.


```abnf
     Expires = HTTP-date
```


   For example

   Expires: Thu, 01 Dec 1994 16:00:00 GMT

> **MUST**: A cache recipient MUST interpret invalid date formats, especially the
   value "0", as representing a time in the past (i.e., "already
   expired").

   If a response includes a Cache-Control header field with the max-age
> **MUST**: directive (Section 5.2.2.1), a recipient MUST ignore the Expires
   header field.  Likewise, if a response includes the s-maxage
> **MUST**: directive (Section 5.2.2.10), a shared cache recipient MUST ignore
   the Expires header field.  In both these cases, the value in Expires
   is only intended for recipients that have not yet implemented the
   Cache-Control header field.

> **MUST NOT**: An origin server without a clock (Section 5.6.7 of [HTTP]) MUST NOT
   generate an Expires header field unless its value represents a fixed
   time in the past (always expired) or its value has been associated
   with the resource by a system with a clock.

   Historically, HTTP required the Expires field value to be no more
   than a year in the future.  While longer freshness lifetimes are no
   longer prohibited, extremely large values have been demonstrated to
   cause problems (e.g., clock overflows due to use of 32-bit integers
   for time values), and many caches will evict a response far sooner
   than that.

---

**Navigation:** [[../RFC9111|RFC9111 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
