---
title: "6.  Response"
rfc_number: 1945
rfc_section: "6"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 6: Response — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request]
---

# 6.  Response


   After receiving and interpreting a request message, a server responds
   in the form of an HTTP response message.


```abnf
       Response        = Simple-Response | Full-Response

       Simple-Response = [ Entity-Body ]
```






```abnf
       Full-Response   = Status-Line             ; Section 6.1
                         *( General-Header       ; Section 4.3
                          | Response-Header      ; Section 6.2
                          | Entity-Header )      ; Section 7.1
                         CRLF
                         [ Entity-Body ]         ; Section 7.2
```


   A Simple-Response should only be sent in response to an HTTP/0.9
   Simple-Request or if the server only supports the more limited
   HTTP/0.9 protocol. If a client sends an HTTP/1.0 Full-Request and
   receives a response that does not begin with a Status-Line, it should
   assume that the response is a Simple-Response and parse it
   accordingly. Note that the Simple-Response consists only of the
   entity body and is terminated by the server closing the connection.

## 6.1  Status-Line

   The first line of a Full-Response message is the Status-Line,
   consisting of the protocol version followed by a numeric status code
   and its associated textual phrase, with each element separated by SP
   characters. No CR or LF is allowed except in the final CRLF sequence.


```abnf
       Status-Line = HTTP-Version SP Status-Code SP Reason-Phrase CRLF
```


   Since a status line always begins with the protocol version and
   status code

       "HTTP/" 1*DIGIT "." 1*DIGIT SP 3DIGIT SP

   (e.g., "HTTP/1.0 200 "), the presence of that expression is
   sufficient to differentiate a Full-Response from a Simple-Response.
   Although the Simple-Response format may allow such an expression to
   occur at the beginning of an entity body, and thus cause a
   misinterpretation of the message if it was given in response to a
   Full-Request, most HTTP/0.9 servers are limited to responses of type
   "text/html" and therefore would never generate such a response.

### 6.1.1  Status Code and Reason Phrase

   The Status-Code element is a 3-digit integer result code of the
   attempt to understand and satisfy the request. The Reason-Phrase is
   intended to give a short textual description of the Status-Code. The
   Status-Code is intended for use by automata and the Reason-Phrase is
   intended for the human user. The client is not required to examine or
   display the Reason-Phrase.






   The first digit of the Status-Code defines the class of response. The
   last two digits do not have any categorization role. There are 5
   values for the first digit:

      o 1xx: Informational - Not used, but reserved for future use

      o 2xx: Success - The action was successfully received,
             understood, and accepted.

      o 3xx: Redirection - Further action must be taken in order to
             complete the request

      o 4xx: Client Error - The request contains bad syntax or cannot
             be fulfilled

      o 5xx: Server Error - The server failed to fulfill an apparently
             valid request

   The individual values of the numeric status codes defined for
   HTTP/1.0, and an example set of corresponding Reason-Phrase's, are
   presented below. The reason phrases listed here are only recommended
   -- they may be replaced by local equivalents without affecting the
   protocol. These codes are fully defined in Section 9.


```abnf
       Status-Code    = "200"   ; OK
                      | "201"   ; Created
                      | "202"   ; Accepted
                      | "204"   ; No Content
                      | "301"   ; Moved Permanently
                      | "302"   ; Moved Temporarily
                      | "304"   ; Not Modified
                      | "400"   ; Bad Request
                      | "401"   ; Unauthorized
                      | "403"   ; Forbidden
                      | "404"   ; Not Found
                      | "500"   ; Internal Server Error
                      | "501"   ; Not Implemented
                      | "502"   ; Bad Gateway
                      | "503"   ; Service Unavailable
                      | extension-code

       extension-code = 3DIGIT

       Reason-Phrase  = *<TEXT, excluding CR, LF>
```


   HTTP status codes are extensible, but the above codes are the only
   ones generally recognized in current practice. HTTP applications are
   not required to understand the meaning of all registered status



   codes, though such understanding is obviously desirable. However,
   applications must understand the class of any status code, as
   indicated by the first digit, and treat any unrecognized response as
   being equivalent to the x00 status code of that class, with the
   exception that an unrecognized response must not be cached. For
   example, if an unrecognized status code of 431 is received by the
   client, it can safely assume that there was something wrong with its
   request and treat the response as if it had received a 400 status
   code. In such cases, user agents should present to the user the
   entity returned with the response, since that entity is likely to
   include human-readable information which will explain the unusual
   status.

## 6.2  Response Header Fields

   The response header fields allow the server to pass additional
   information about the response which cannot be placed in the Status-
   Line. These header fields give information about the server and about
   further access to the resource identified by the Request-URI.


```abnf
       Response-Header = Location                ; Section 10.11
                       | Server                  ; Section 10.14
                       | WWW-Authenticate        ; Section 10.16
```


   Response-Header field names can be extended reliably only in
   combination with a change in the protocol version. However, new or
   experimental header fields may be given the semantics of response
   header fields if all parties in the communication recognize them to
    be response header fields. Unrecognized header fields are treated as
   Entity-Header fields.

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
