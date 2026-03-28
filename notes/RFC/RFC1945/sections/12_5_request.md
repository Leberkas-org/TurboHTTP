---
title: "5.  Request"
rfc_number: 1945
rfc_section: "5"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 5: Request — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request]
---

# 5.  Request



   A request message from a client to a server includes, within the
   first line of that message, the method to be applied to the resource,
   the identifier of the resource, and the protocol version in use. For
   backwards compatibility with the more limited HTTP/0.9 protocol,
   there are two valid formats for an HTTP request:


```abnf
       Request        = Simple-Request | Full-Request

       Simple-Request = "GET" SP Request-URI CRLF

       Full-Request   = Request-Line             ; Section 5.1
                        *( General-Header        ; Section 4.3
                         | Request-Header        ; Section 5.2
                         | Entity-Header )       ; Section 7.1
                        CRLF
                        [ Entity-Body ]          ; Section 7.2
```


   If an HTTP/1.0 server receives a Simple-Request, it must respond with
   an HTTP/0.9 Simple-Response. An HTTP/1.0 client capable of receiving
   a Full-Response should never generate a Simple-Request.

## 5.1  Request-Line

   The Request-Line begins with a method token, followed by the
   Request-URI and the protocol version, and ending with CRLF. The
   elements are separated by SP characters. No CR or LF are allowed
   except in the final CRLF sequence.


```abnf
       Request-Line = Method SP Request-URI SP HTTP-Version CRLF
```




   Note that the difference between a Simple-Request and the Request-
   Line of a Full-Request is the presence of the HTTP-Version field and
   the availability of methods other than GET.

### 5.1.1  Method

   The Method token indicates the method to be performed on the resource
   identified by the Request-URI. The method is case-sensitive.


```abnf
       Method         = "GET"                    ; Section 8.1
                      | "HEAD"                   ; Section 8.2
                      | "POST"                   ; Section 8.3
                      | extension-method

       extension-method = token
```


   The list of methods acceptable by a specific resource can change
   dynamically; the client is notified through the return code of the
   response if a method is not allowed on a resource. Servers should
   return the status code 501 (not implemented) if the method is
   unrecognized or not implemented.

   The methods commonly used by HTTP/1.0 applications are fully defined
   in Section 8.

### 5.1.2  Request-URI

   The Request-URI is a Uniform Resource Identifier (Section 3.2) and
   identifies the resource upon which to apply the request.


```abnf
       Request-URI    = absoluteURI | abs_path
```


   The two options for Request-URI are dependent on the nature of the
   request.

   The absoluteURI form is only allowed when the request is being made
   to a proxy. The proxy is requested to forward the request and return
   the response. If the request is GET or HEAD and a prior response is
   cached, the proxy may use the cached message if it passes any
   restrictions in the Expires header field. Note that the proxy may
   forward the request on to another proxy or directly to the server
   specified by the absoluteURI. In order to avoid request loops, a
   proxy must be able to recognize all of its server names, including
   any aliases, local variations, and the numeric IP address. An example
   Request-Line would be:

       GET http://www.w3.org/pub/WWW/TheProject.html HTTP/1.0




   The most common form of Request-URI is that used to identify a
   resource on an origin server or gateway. In this case, only the
   absolute path of the URI is transmitted (see Section 3.2.1,
   abs_path). For example, a client wishing to retrieve the resource
   above directly from the origin server would create a TCP connection
   to port 80 of the host "www.w3.org" and send the line:

       GET /pub/WWW/TheProject.html HTTP/1.0

   followed by the remainder of the Full-Request. Note that the absolute
   path cannot be empty; if none is present in the original URI, it must
   be given as "/" (the server root).

   The Request-URI is transmitted as an encoded string, where some
   characters may be escaped using the "% HEX HEX" encoding defined by
   RFC 1738 [4]. The origin server must decode the Request-URI in order
   to properly interpret the request.

## 5.2  Request Header Fields

   The request header fields allow the client to pass additional
   information about the request, and about the client itself, to the
   server. These fields act as request modifiers, with semantics
   equivalent to the parameters on a programming language method
   (procedure) invocation.


```abnf
       Request-Header = Authorization            ; Section 10.2
                      | From                     ; Section 10.8
                      | If-Modified-Since        ; Section 10.9
                      | Referer                  ; Section 10.13
                      | User-Agent               ; Section 10.15
```


   Request-Header field names can be extended reliably only in
   combination with a change in the protocol version. However, new or
   experimental header fields may be given the semantics of request
   header fields if all parties in the communication recognize them to
   be request header fields. Unrecognized header fields are treated as
   Entity-Header fields.

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
