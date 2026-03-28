---
title: "10.9.  If-Modified-Since"
rfc_number: 1945
rfc_section: "10.9"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 10.9: If-Modified-Since — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request, if-modified-since]
---

# 10.9.  If-Modified-Since

## 10.9  If-Modified-Since

   The If-Modified-Since request-header field is used with the GET
   method to make it conditional: if the requested resource has not been
   modified since the time specified in this field, a copy of the
   resource will not be returned from the server; instead, a 304 (not
   modified) response will be returned without any Entity-Body.


```abnf
       If-Modified-Since = "If-Modified-Since" ":" HTTP-date
```


   An example of the field is:

       If-Modified-Since: Sat, 29 Oct 1994 19:43:31 GMT





   A conditional GET method requests that the identified resource be
   transferred only if it has been modified since the date given by the
   If-Modified-Since header. The algorithm for determining this includes
   the following cases:

      a) If the request would normally result in anything other than
         a 200 (ok) status, or if the passed If-Modified-Since date
         is invalid, the response is exactly the same as for a
         normal GET. A date which is later than the server's current
         time is invalid.

      b) If the resource has been modified since the
         If-Modified-Since date, the response is exactly the same as
         for a normal GET.

      c) If the resource has not been modified since a valid
         If-Modified-Since date, the server shall return a 304 (not
         modified) response.

   The purpose of this feature is to allow efficient updates of cached
   information with a minimum amount of transaction overhead.

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
