---
title: "10.13.  Referer"
rfc_number: 1945
rfc_section: "10.13"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 10.13: Referer — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request, referer]
---

# 10.13.  Referer

## 10.13  Referer

   The Referer request-header field allows the client to specify, for
   the server's benefit, the address (URI) of the resource from which
   the Request-URI was obtained. This allows a server to generate lists



   of back-links to resources for interest, logging, optimized caching,
   etc. It also allows obsolete or mistyped links to be traced for
   maintenance. The Referer field must not be sent if the Request-URI
   was obtained from a source that does not have its own URI, such as
   input from the user keyboard.


```abnf
       Referer        = "Referer" ":" ( absoluteURI | relativeURI )
```


   Example:

       Referer: http://www.w3.org/hypertext/DataSources/Overview.html

   If a partial URI is given, it should be interpreted relative to the
   Request-URI. The URI must not include a fragment.

      Note: Because the source of a link may be private information or
      may reveal an otherwise private information source, it is strongly
      recommended that the user be able to select whether or not the
      Referer field is sent. For example, a browser client could have a
      toggle switch for browsing openly/anonymously, which would
      respectively enable/disable the sending of Referer and From
      information.

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
