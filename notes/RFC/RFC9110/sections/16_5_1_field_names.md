---
title: "5.1.  Field Names"
rfc_number: 9110
rfc_section: "5.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 5.1: Field Names — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, field_names]
---

## 5.1.  Field Names

5.  Fields

   HTTP uses "fields" to provide data in the form of extensible name/
   value pairs with a registered key namespace.  Fields are sent and
   received within the header and trailer sections of messages
   (Section 6).

## 5.1  Field Names

   A field name labels the corresponding field value as having the
   semantics defined by that name.  For example, the Date header field
   is defined in Section 6.6.1 as containing the origination timestamp
   for the message in which it appears.


```abnf
     field-name     = token
```


   Field names are case-insensitive and ought to be registered within
   the "Hypertext Transfer Protocol (HTTP) Field Name Registry"; see
   Section 16.3.1.

   The interpretation of a field does not change between minor versions
   of the same major HTTP version, though the default behavior of a
   recipient in the absence of such a field can change.  Unless
   specified otherwise, fields are defined for all versions of HTTP.  In
   particular, the Host and Connection fields ought to be recognized by
   all HTTP implementations whether or not they advertise conformance
   with HTTP/1.1.

   New fields can be introduced without changing the protocol version if
   their defined semantics allow them to be safely ignored by recipients
   that do not recognize them; see Section 16.3.

> **MUST**: A proxy MUST forward unrecognized header fields unless the field name
   is listed in the Connection header field (Section 7.6.1) or the proxy
   is specifically configured to block, or otherwise transform, such
> **SHOULD**: fields.  Other recipients SHOULD ignore unrecognized header and
   trailer fields.  Adhering to these requirements allows HTTP's
   functionality to be extended without updating or removing deployed
   intermediaries.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
