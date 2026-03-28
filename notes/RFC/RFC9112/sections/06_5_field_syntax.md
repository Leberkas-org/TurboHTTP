---
title: 5.  Field Syntax
rfc_number: 9112
rfc_section: '5'
source_url: 'https://www.rfc-editor.org/rfc/rfc9112'
description: 'Section 5: Field Syntax — RFC 9112 — HTTP/1.1'
tags:
  - RFC9112
  - HTTP/1.1
  - message-framing
  - chunked-encoding
  - connection-management
  - keep-alive
  - Host-header
  - pipelining
  - field_syntax
---

## 5.  Field Syntax

5.  Field Syntax

   Each field line consists of a case-insensitive field name followed by
   a colon (":"), optional leading whitespace, the field line value, and
   optional trailing whitespace.


```abnf
     field-line   = field-name ":" OWS field-value OWS
```


   Rules for parsing within field values are defined in Section 5.5 of
   [HTTP].  This section covers the generic syntax for header field
   inclusion within, and extraction from, HTTP/1.1 messages.

## 5.1  Field Line Parsing

   Messages are parsed using a generic algorithm, independent of the
   individual field names.  The contents within a given field line value
   are not parsed until a later stage of message interpretation (usually
   after the message's entire field section has been processed).

   No whitespace is allowed between the field name and colon.  In the
   past, differences in the handling of such whitespace have led to
   security vulnerabilities in request routing and response handling.  A
> **MUST**: server MUST reject, with a response status code of 400 (Bad Request),
   any received request message that contains whitespace between a
> **MUST**: header field name and colon.  A proxy MUST remove any such whitespace
   from a response message before forwarding the message downstream.

   A field line value might be preceded and/or followed by optional
   whitespace (OWS); a single SP preceding the field line value is
   preferred for consistent readability by humans.  The field line value
   does not include that leading or trailing whitespace: OWS occurring
   before the first non-whitespace octet of the field line value, or
   after the last non-whitespace octet of the field line value, is
   excluded by parsers when extracting the field line value from a field
   line.

## 5.2  Obsolete Line Folding

   Historically, HTTP/1.x field values could be extended over multiple
   lines by preceding each extra line with at least one space or
   horizontal tab (obs-fold).  This specification deprecates such line
   folding except within the "message/http" media type (Section 10.1).


```abnf
     obs-fold     = OWS CRLF RWS
                  ; obsolete line folding
```


> **MUST NOT**: A sender MUST NOT generate a message that includes line folding
   (i.e., that has any field line value that contains a match to the
   obs-fold rule) unless the message is intended for packaging within
   the "message/http" media type.

   A server that receives an obs-fold in a request message that is not
> **MUST**: within a "message/http" container MUST either reject the message by
   sending a 400 (Bad Request), preferably with a representation
   explaining that obsolete line folding is unacceptable, or replace
   each received obs-fold with one or more SP octets prior to
   interpreting the field value or forwarding the message downstream.

   A proxy or gateway that receives an obs-fold in a response message
> **MUST**: that is not within a "message/http" container MUST either discard the
   message and replace it with a 502 (Bad Gateway) response, preferably
   with a representation explaining that unacceptable line folding was
   received, or replace each received obs-fold with one or more SP
   octets prior to interpreting the field value or forwarding the
   message downstream.

   A user agent that receives an obs-fold in a response message that is
> **MUST**: not within a "message/http" container MUST replace each received
   obs-fold with one or more SP octets prior to interpreting the field
   value.


---

## TurboHttp Compliance

**Status:** ✅ Compliant

**Implementation Notes:**
TurboHttp's HTTP/1.1 decoder correctly parses field lines as `field-name ":" OWS field-value OWS`. Leading and trailing whitespace around field values is trimmed. Field names are treated case-insensitively. Obsolete line folding (obs-fold) is handled by replacing with SP octets.

**Key Components:**
- `Http11ResponseDecoder` — header field parsing and extraction
- `Http11RequestEncoder` — header field serialization

**Compliance Details:**
- ✅ Field-line format: `field-name ":" OWS field-value OWS`
- ✅ Whitespace between field-name and colon rejected (as client, not generated)
- ✅ Leading/trailing OWS trimmed from field values
- ✅ Obs-fold replaced with SP when encountered
- ✅ Case-insensitive field name handling

**Gaps:** None identified

**Test References:** `TurboHttp.Tests.RFC9112`

---

**Navigation:** [[../RFC9112|RFC9112 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
