---
title: "10.4.  Content-Length"
rfc_number: 1945
rfc_section: "10.4"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 10.4: Content-Length — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request]
---

# 10.4.  Content-Length

## 10.4  Content-Length

   The Content-Length entity-header field indicates the size of the
   Entity-Body, in decimal number of octets, sent to the recipient or,
   in the case of the HEAD method, the size of the Entity-Body that
   would have been sent had the request been a GET.


```abnf
       Content-Length = "Content-Length" ":" 1*DIGIT
```


   An example is

       Content-Length: 3495

   Applications should use this field to indicate the size of the
   Entity-Body to be transferred, regardless of the media type of the
   entity. A valid Content-Length field value is required on all
   HTTP/1.0 request messages containing an entity body.

   Any Content-Length greater than or equal to zero is a valid value.
   Section 7.2.2 describes how to determine the length of a response
   entity body if a Content-Length is not given.

      Note: The meaning of this field is significantly different from
      the corresponding definition in MIME, where it is an optional
      field used within the "message/external-body" content-type. In
      HTTP, it should be used whenever the entity's length can be
      determined prior to being transferred.

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
