---
title: "10.5.  Content-Type"
rfc_number: 1945
rfc_section: "10.5"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 10.5: Content-Type — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request, content-type]
---

# 10.5.  Content-Type

## 10.5  Content-Type

   The Content-Type entity-header field indicates the media type of the
   Entity-Body sent to the recipient or, in the case of the HEAD method,
   the media type that would have been sent had the request been a GET.


```abnf
       Content-Type   = "Content-Type" ":" media-type
```


   Media types are defined in Section 3.6. An example of the field is

       Content-Type: text/html

   Further discussion of methods for identifying the media type of an
   entity is provided in Section 7.2.1.

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
