---
title: "12.4.  Content Negotiation Field Features"
rfc_number: 9110
rfc_section: "12.4"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 12.4: Content Negotiation Field Features — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, content_negotiation_field_features]
---

## 12.4.  Content Negotiation Field Features

## 12.4  Content Negotiation Field Features

### 12.4.1  Absence

   For each of the content negotiation fields, a request that does not
   contain the field implies that the sender has no preference on that
   dimension of negotiation.

   If a content negotiation header field is present in a request and
   none of the available representations for the response can be
   considered acceptable according to it, the origin server can either
   honor the header field by sending a 406 (Not Acceptable) response or
   disregard the header field by treating the response as if it is not
   subject to content negotiation for that request header field.  This
   does not imply, however, that the client will be able to use the
   representation.

      |  *Note:* A user agent sending these header fields makes it
      |  easier for a server to identify an individual by virtue of the
      |  user agent's request characteristics (Section 17.13).

### 12.4.2  Quality Values

   The content negotiation fields defined by this specification use a
   common parameter, named "q" (case-insensitive), to assign a relative
   "weight" to the preference for that associated kind of content.  This
   weight is referred to as a "quality value" (or "qvalue") because the
   same parameter name is often used within server configurations to
   assign a weight to the relative quality of the various
   representations that can be selected for a resource.

   The weight is normalized to a real number in the range 0 through 1,
   where 0.001 is the least preferred and 1 is the most preferred; a
   value of 0 means "not acceptable".  If no "q" parameter is present,
   the default weight is 1.


```abnf
     weight = OWS ";" OWS "q=" qvalue
     qvalue = ( "0" [ "." 0*3DIGIT ] )
            / ( "1" [ "." 0*3("0") ] )
```


> **MUST NOT**: A sender of qvalue MUST NOT generate more than three digits after the
   decimal point.  User configuration of these values ought to be
   limited in the same fashion.

### 12.4.3  Wildcard Values

   Most of these header fields, where indicated, define a wildcard value
   ("*") to select unspecified values.  If no wildcard is present,
   values that are not explicitly mentioned in the field are considered
   unacceptable.  Within Vary, the wildcard value means that the
   variance is unlimited.

      |  *Note:* In practice, using wildcards in content negotiation has
      |  limited practical value because it is seldom useful to say, for
      |  example, "I prefer image/* more or less than (some other
      |  specific value)".  By sending Accept: */*;q=0, clients can
      |  explicitly request a 406 (Not Acceptable) response if a more
      |  preferred format is not available, but they still need to be
      |  able to handle a different response since the server is allowed
      |  to ignore their preference.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
