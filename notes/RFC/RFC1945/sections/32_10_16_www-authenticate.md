---
title: "10.16.  WWW-Authenticate"
rfc_number: 1945
rfc_section: "10.16"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 10.16: WWW-Authenticate — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request, www-authenticate]
---

# 10.16.  WWW-Authenticate

## 10.16  WWW-Authenticate

   The WWW-Authenticate response-header field must be included in 401
   (unauthorized) response messages. The field value consists of at
   least one challenge that indicates the authentication scheme(s) and
   parameters applicable to the Request-URI.


```abnf
       WWW-Authenticate = "WWW-Authenticate" ":" 1#challenge
```


   The HTTP access authentication process is described in Section 11.
   User agents must take special care in parsing the WWW-Authenticate
   field value if it contains more than one challenge, or if more than
   one WWW-Authenticate header field is provided, since the contents of
   a challenge may itself contain a comma-separated list of
   authentication parameters.

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
