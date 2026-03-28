---
title: "4.  HTTP Message"
rfc_number: 1945
rfc_section: "4"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 4: HTTP Message — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request, http_message]
---

# 4.  HTTP Message


## 4.1  Message Types

   HTTP messages consist of requests from client to server and responses
   from server to client.


```abnf
       HTTP-message   = Simple-Request           ; HTTP/0.9 messages
                      | Simple-Response
                      | Full-Request             ; HTTP/1.0 messages
                      | Full-Response
```


   Full-Request and Full-Response use the generic message format of RFC
   822 [7] for transferring entities. Both messages may include optional
   header fields (also known as "headers") and an entity body. The
   entity body is separated from the headers by a null line (i.e., a
   line with nothing preceding the CRLF).


```abnf
       Full-Request   = Request-Line             ; Section 5.1
                        *( General-Header        ; Section 4.3
                         | Request-Header        ; Section 5.2
                         | Entity-Header )       ; Section 7.1
                        CRLF
                        [ Entity-Body ]          ; Section 7.2

       Full-Response  = Status-Line              ; Section 6.1
                        *( General-Header        ; Section 4.3
                         | Response-Header       ; Section 6.2
```




                         | Entity-Header )       ; Section 7.1
                        CRLF
                        [ Entity-Body ]          ; Section 7.2

   Simple-Request and Simple-Response do not allow the use of any header
   information and are limited to a single request method (GET).


```abnf
       Simple-Request  = "GET" SP Request-URI CRLF

       Simple-Response = [ Entity-Body ]
```


   Use of the Simple-Request format is discouraged because it prevents
   the server from identifying the media type of the returned entity.

## 4.2  Message Headers

   HTTP header fields, which include General-Header (Section 4.3),
   Request-Header (Section 5.2), Response-Header (Section 6.2), and
   Entity-Header (Section 7.1) fields, follow the same generic format as
   that given in Section 3.1 of RFC 822 [7]. Each header field consists
   of a name followed immediately by a colon (":"), a single space (SP)
   character, and the field value. Field names are case-insensitive.
   Header fields can be extended over multiple lines by preceding each
   extra line with at least one SP or HT, though this is not
   recommended.


```abnf
       HTTP-header    = field-name ":" [ field-value ] CRLF

       field-name     = token
       field-value    = *( field-content | LWS )

       field-content  = <the OCTETs making up the field-value
                        and consisting of either *TEXT or combinations
                        of token, tspecials, and quoted-string>
```


   The order in which header fields are received is not significant.
   However, it is "good practice" to send General-Header fields first,
   followed by Request-Header or Response-Header fields prior to the
   Entity-Header fields.

   Multiple HTTP-header fields with the same field-name may be present
   in a message if and only if the entire field-value for that header
   field is defined as a comma-separated list [i.e., #(values)]. It must
   be possible to combine the multiple header fields into one "field-
   name: field-value" pair, without changing the semantics of the
   message, by appending each subsequent field-value to the first, each
   separated by a comma.




## 4.3  General Header Fields

   There are a few header fields which have general applicability for
   both request and response messages, but which do not apply to the
   entity being transferred. These headers apply only to the message
   being transmitted.


```abnf
       General-Header = Date                     ; Section 10.6
                      | Pragma                   ; Section 10.12
```


   General header field names can be extended reliably only in
   combination with a change in the protocol version. However, new or
   experimental header fields may be given the semantics of general
   header fields if all parties in the communication recognize them to
   be general header fields. Unrecognized header fields are treated as
   Entity-Header fields.

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
