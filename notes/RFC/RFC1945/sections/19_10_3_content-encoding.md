---
title: "10.3.  Content-Encoding"
rfc_number: 1945
rfc_section: "10.3"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 10.3: Content-Encoding — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request, content-encoding]
---

# 10.3.  Content-Encoding

## 10.3  Content-Encoding

   The Content-Encoding entity-header field is used as a modifier to the
   media-type. When present, its value indicates what additional content
   coding has been applied to the resource, and thus what decoding
   mechanism must be applied in order to obtain the media-type
   referenced by the Content-Type header field. The Content-Encoding is
   primarily used to allow a document to be compressed without losing
   the identity of its underlying media type.


```abnf
       Content-Encoding = "Content-Encoding" ":" content-coding
```


   Content codings are defined in Section 3.5. An example of its use is

       Content-Encoding: x-gzip

   The Content-Encoding is a characteristic of the resource identified
   by the Request-URI. Typically, the resource is stored with this
   encoding and is only decoded before rendering or analogous usage.

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
