---
title: "7.  Entity"
rfc_number: 1945
rfc_section: "7"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 7: Entity — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request]
---

# 7.  Entity


   Full-Request and Full-Response messages may transfer an entity within
   some requests and responses. An entity consists of Entity-Header
   fields and (usually) an Entity-Body. In this section, both sender and
   recipient refer to either the client or the server, depending on who
   sends and who receives the entity.













## 7.1  Entity Header Fields

   Entity-Header fields define optional metainformation about the
   Entity-Body or, if no body is present, about the resource identified
   by the request.


```abnf
       Entity-Header  = Allow                    ; Section 10.1
                      | Content-Encoding         ; Section 10.3
                      | Content-Length           ; Section 10.4
                      | Content-Type             ; Section 10.5
                      | Expires                  ; Section 10.7
                      | Last-Modified            ; Section 10.10
                      | extension-header

       extension-header = HTTP-header
```


   The extension-header mechanism allows additional Entity-Header fields
   to be defined without changing the protocol, but these fields cannot
   be assumed to be recognizable by the recipient. Unrecognized header
   fields should be ignored by the recipient and forwarded by proxies.

## 7.2  Entity Body

   The entity body (if any) sent with an HTTP request or response is in
   a format and encoding defined by the Entity-Header fields.


```abnf
       Entity-Body    = *OCTET
```


   An entity body is included with a request message only when the
   request method calls for one. The presence of an entity body in a
   request is signaled by the inclusion of a Content-Length header field
   in the request message headers. HTTP/1.0 requests containing an
   entity body must include a valid Content-Length header field.

   For response messages, whether or not an entity body is included with
   a message is dependent on both the request method and the response
   code. All responses to the HEAD request method must not include a
   body, even though the presence of entity header fields may lead one
   to believe they do. All 1xx (informational), 204 (no content), and
   304 (not modified) responses must not include a body. All other
   responses must include an entity body or a Content-Length header
   field defined with a value of zero (0).

### 7.2.1  Type

   When an Entity-Body is included with a message, the data type of that
   body is determined via the header fields Content-Type and Content-
   Encoding. These define a two-layer, ordered encoding model:



       entity-body := Content-Encoding( Content-Type( data ) )

   A Content-Type specifies the media type of the underlying data. A
   Content-Encoding may be used to indicate any additional content
   coding applied to the type, usually for the purpose of data
   compression, that is a property of the resource requested. The
   default for the content encoding is none (i.e., the identity
   function).

   Any HTTP/1.0 message containing an entity body should include a
   Content-Type header field defining the media type of that body. If
   and only if the media type is not given by a Content-Type header, as
   is the case for Simple-Response messages, the recipient may attempt
   to guess the media type via inspection of its content and/or the name
   extension(s) of the URL used to identify the resource. If the media
   type remains unknown, the recipient should treat it as type
   "application/octet-stream".

### 7.2.2  Length

   When an Entity-Body is included with a message, the length of that
   body may be determined in one of two ways. If a Content-Length header
   field is present, its value in bytes represents the length of the
   Entity-Body. Otherwise, the body length is determined by the closing
   of the connection by the server.

   Closing the connection cannot be used to indicate the end of a
   request body, since it leaves no possibility for the server to send
   back a response. Therefore, HTTP/1.0 requests containing an entity
   body must include a valid Content-Length header field. If a request
   contains an entity body and Content-Length is not specified, and the
   server does not recognize or cannot calculate the length from other
   fields, then the server should send a 400 (bad request) response.

      Note: Some older servers supply an invalid Content-Length when
      sending a document that contains server-side includes dynamically
      inserted into the data stream. It must be emphasized that this
      will not be tolerated by future versions of HTTP. Unless the
      client knows that it is receiving a response from a compliant
      server, it should not depend on the Content-Length value being
      correct.

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
