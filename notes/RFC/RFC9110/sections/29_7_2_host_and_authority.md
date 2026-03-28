---
title: "7.2.  Host and :authority"
rfc_number: 9110
rfc_section: "7.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 7.2: Host and :authority — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, host_and_authority]
---

## 7.2.  Host and :authority

## 7.2  Host and :authority

   The "Host" header field in a request provides the host and port
   information from the target URI, enabling the origin server to
   distinguish among resources while servicing requests for multiple
   host names.

   In HTTP/2 [HTTP/2] and HTTP/3 [HTTP/3], the Host header field is, in
   some cases, supplanted by the ":authority" pseudo-header field of a
   request's control data.


```abnf
     Host = uri-host [ ":" port ] ; Section 4
```


   The target URI's authority information is critical for handling a
> **MUST**: request.  A user agent MUST generate a Host header field in a request
   unless it sends that information as an ":authority" pseudo-header
> **SHOULD**: field.  A user agent that sends Host SHOULD send it as the first
   field in the header section of a request.

   For example, a GET request to the origin server for
   <http://www.example.org/pub/WWW/> would begin with:

   GET /pub/WWW/ HTTP/1.1
   Host: www.example.org

   Since the host and port information acts as an application-level
   routing mechanism, it is a frequent target for malware seeking to
   poison a shared cache or redirect a request to an unintended server.
   An interception proxy is particularly vulnerable if it relies on the
   host and port information for redirecting requests to internal
   servers, or for use as a cache key in a shared cache, without first
   verifying that the intercepted connection is targeting a valid IP
   address for that host.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
