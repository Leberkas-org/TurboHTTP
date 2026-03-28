---
title: "10.2.  Authorization"
rfc_number: 1945
rfc_section: "10.2"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 10.2: Authorization — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request, authorization]
---

# 10.2.  Authorization

## 10.2  Authorization

   A user agent that wishes to authenticate itself with a server--
   usually, but not necessarily, after receiving a 401 response--may do
   so by including an Authorization request-header field with the
   request. The Authorization field value consists of credentials
   containing the authentication information of the user agent for the
   realm of the resource being requested.


```abnf
       Authorization  = "Authorization" ":" credentials
```


   HTTP access authentication is described in Section 11. If a request
   is authenticated and a realm specified, the same credentials should
   be valid for all other requests within this realm.

   Responses to requests containing an Authorization field are not
   cachable.

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
