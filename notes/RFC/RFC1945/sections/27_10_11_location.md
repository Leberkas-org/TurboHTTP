---
title: "10.11.  Location"
rfc_number: 1945
rfc_section: "10.11"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 10.11: Location — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request, location]
---

# 10.11.  Location

## 10.11  Location

   The Location response-header field defines the exact location of the
   resource that was identified by the Request-URI. For 3xx responses,
   the location must indicate the server's preferred URL for automatic
   redirection to the resource. Only one absolute URL is allowed.


```abnf
       Location       = "Location" ":" absoluteURI
```


   An example is

       Location: http://www.w3.org/hypertext/WWW/NewLocation.html

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
