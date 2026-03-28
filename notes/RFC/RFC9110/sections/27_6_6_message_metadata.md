---
title: "6.6.  Message Metadata"
rfc_number: 9110
rfc_section: "6.6"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 6.6: Message Metadata — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, message_metadata]
---

## 6.6.  Message Metadata

## 6.6  Message Metadata

   Fields that describe the message itself, such as when and how the
   message has been generated, can appear in both requests and
   responses.

### 6.6.1  Date

   The "Date" header field represents the date and time at which the
   message was originated, having the same semantics as the Origination
   Date Field (orig-date) defined in Section 3.6.1 of [RFC5322].  The
   field value is an HTTP-date, as defined in Section 5.6.7.


```abnf
     Date = HTTP-date
```


   An example is

   Date: Tue, 15 Nov 1994 08:12:31 GMT

> **SHOULD**: A sender that generates a Date header field SHOULD generate its field
   value as the best available approximation of the date and time of
   message generation.  In theory, the date ought to represent the
   moment just before generating the message content.  In practice, a
   sender can generate the date value at any time during message
   origination.

> **MUST**: An origin server with a clock (as defined in Section 5.6.7) MUST
   generate a Date header field in all 2xx (Successful), 3xx
> **MAY**: (Redirection), and 4xx (Client Error) responses, and MAY generate a
   Date header field in 1xx (Informational) and 5xx (Server Error)
   responses.

> **MUST NOT**: An origin server without a clock MUST NOT generate a Date header
   field.

   A recipient with a clock that receives a response message without a
> **MUST**: Date header field MUST record the time it was received and append a
   corresponding Date header field to the message's header section if it
   is cached or forwarded downstream.

   A recipient with a clock that receives a response with an invalid
> **MAY**: Date header field value MAY replace that value with the time that
   response was received.

> **MAY**: A user agent MAY send a Date header field in a request, though
   generally will not do so unless it is believed to convey useful
   information to the server.  For example, custom applications of HTTP
   might convey a Date if the server is expected to adjust its
   interpretation of the user's request based on differences between the
   user agent and server clocks.

### 6.6.2  Trailer

   The "Trailer" header field provides a list of field names that the
   sender anticipates sending as trailer fields within that message.
   This allows a recipient to prepare for receipt of the indicated
   metadata before it starts processing the content.


```abnf
     Trailer = #field-name
```


   For example, a sender might indicate that a signature will be
   computed as the content is being streamed and provide the final
   signature as a trailer field.  This allows a recipient to perform the
   same check on the fly as it receives the content.

   A sender that intends to generate one or more trailer fields in a
> **SHOULD**: message SHOULD generate a Trailer header field in the header section
   of that message to indicate which fields might be present in the
   trailers.

   If an intermediary discards the trailer section in transit, the
   Trailer field could provide a hint of what metadata was lost, though
   there is no guarantee that a sender of Trailer will always follow
   through by sending the named fields.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
