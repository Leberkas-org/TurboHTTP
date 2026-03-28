---
title: "15.6.  Server Error 5xx"
rfc_number: 9110
rfc_section: "15.6"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 15.6: Server Error 5xx — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, server_error_5xx]
---

## 15.6.  Server Error 5xx

## 15.6  Server Error 5xx

   The 5xx (Server Error) class of status code indicates that the server
   is aware that it has erred or is incapable of performing the
   requested method.  Except when responding to a HEAD request, the
> **SHOULD**: server SHOULD send a representation containing an explanation of the
   error situation, and whether it is a temporary or permanent
> **SHOULD**: condition.  A user agent SHOULD display any included representation
   to the user.  These status codes are applicable to any request
   method.

15.6.1.  500 Internal Server Error

   The 500 (Internal Server Error) status code indicates that the server
   encountered an unexpected condition that prevented it from fulfilling
   the request.

15.6.2.  501 Not Implemented

   The 501 (Not Implemented) status code indicates that the server does
   not support the functionality required to fulfill the request.  This
   is the appropriate response when the server does not recognize the
   request method and is not capable of supporting it for any resource.

   A 501 response is heuristically cacheable; i.e., unless otherwise
   indicated by the method definition or explicit cache controls (see
   Section 4.2.2 of [CACHING]).

15.6.3.  502 Bad Gateway

   The 502 (Bad Gateway) status code indicates that the server, while
   acting as a gateway or proxy, received an invalid response from an
   inbound server it accessed while attempting to fulfill the request.

15.6.4.  503 Service Unavailable

   The 503 (Service Unavailable) status code indicates that the server
   is currently unable to handle the request due to a temporary overload
   or scheduled maintenance, which will likely be alleviated after some
> **MAY**: delay.  The server MAY send a Retry-After header field
   (Section 10.2.3) to suggest an appropriate amount of time for the
   client to wait before retrying the request.

      |  *Note:* The existence of the 503 status code does not imply
      |  that a server has to use it when becoming overloaded.  Some
      |  servers might simply refuse the connection.

15.6.5.  504 Gateway Timeout

   The 504 (Gateway Timeout) status code indicates that the server,
   while acting as a gateway or proxy, did not receive a timely response
   from an upstream server it needed to access in order to complete the
   request.

15.6.6.  505 HTTP Version Not Supported

   The 505 (HTTP Version Not Supported) status code indicates that the
   server does not support, or refuses to support, the major version of
   HTTP that was used in the request message.  The server is indicating
   that it is unable or unwilling to complete the request using the same
   major version as the client, as described in Section 2.5, other than
> **SHOULD**: with this error message.  The server SHOULD generate a representation
   for the 505 response that describes why that version is not supported
   and what other protocols are supported by that server.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
