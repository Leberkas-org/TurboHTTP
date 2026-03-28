---
title: "10.15.  User-Agent"
rfc_number: 1945
rfc_section: "10.15"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 10.15: User-Agent — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request, user-agent]
---

# 10.15.  User-Agent

## 10.15  User-Agent

   The User-Agent request-header field contains information about the
   user agent originating the request. This is for statistical purposes,
   the tracing of protocol violations, and automated recognition of user
   agents for the sake of tailoring responses to avoid particular user
   agent limitations. Although it is not required, user agents should
   include this field with requests. The field can contain multiple
   product tokens (Section 3.7) and comments identifying the agent and
   any subproducts which form a significant part of the user agent. By
   convention, the product tokens are listed in order of their
   significance for identifying the application.


```abnf
       User-Agent     = "User-Agent" ":" 1*( product | comment )
```


   Example:

       User-Agent: CERN-LineMode/2.15 libwww/2.17b3

       Note: Some current proxy applications append their product
       information to the list in the User-Agent field. This is not
       recommended, since it makes machine interpretation of these
       fields ambiguous.

       Note: Some existing clients fail to restrict themselves to
       the product token syntax within the User-Agent field.

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
