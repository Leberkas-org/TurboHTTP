---
title: 4.  Status Line
rfc_number: 9112
rfc_section: '4'
source_url: 'https://www.rfc-editor.org/rfc/rfc9112'
description: 'Section 4: Status Line — RFC 9112 — HTTP/1.1'
tags:
  - RFC9112
  - HTTP/1.1
  - message-framing
  - chunked-encoding
  - connection-management
  - keep-alive
  - Host-header
  - pipelining
  - status_line
---

## 4.  Status Line

4.  Status Line

   The first line of a response message is the status-line, consisting
   of the protocol version, a space (SP), the status code, and another
   space and ending with an OPTIONAL textual phrase describing the
   status code.


```abnf
     status-line = HTTP-version SP status-code SP [ reason-phrase ]
```


   Although the status-line grammar rule requires that each of the
> **MAY**: component elements be separated by a single SP octet, recipients MAY
   instead parse on whitespace-delimited word boundaries and, aside from
   the line terminator, treat any form of whitespace as the SP separator
   while ignoring preceding or trailing whitespace; such whitespace
   includes one or more of the following octets: SP, HTAB, VT (%x0B), FF
   (%x0C), or bare CR.  However, lenient parsing can result in response
   splitting security vulnerabilities if there are multiple recipients
   of the message and each has its own unique interpretation of
   robustness (see Section 11.1).

   The status-code element is a 3-digit integer code describing the
   result of the server's attempt to understand and satisfy the client's
   corresponding request.  A recipient parses and interprets the
   remainder of the response message in light of the semantics defined
   for that status code, if the status code is recognized by that
   recipient, or in accordance with the class of that status code when
   the specific code is unrecognized.


```abnf
     status-code    = 3DIGIT
```


   HTTP's core status codes are defined in Section 15 of [HTTP], along
   with the classes of status codes, considerations for the definition
   of new status codes, and the IANA registry for collecting such
   definitions.

   The reason-phrase element exists for the sole purpose of providing a
   textual description associated with the numeric status code, mostly
   out of deference to earlier Internet application protocols that were
   more frequently used with interactive text clients.


```abnf
     reason-phrase  = 1*( HTAB / SP / VCHAR / obs-text )
```


> **SHOULD**: A client SHOULD ignore the reason-phrase content because it is not a
   reliable channel for information (it might be translated for a given
   locale, overwritten by intermediaries, or discarded when the message
> **MUST**: is forwarded via other versions of HTTP).  A server MUST send the
   space that separates the status-code from the reason-phrase even when
   the reason-phrase is absent (i.e., the status-line would end with the
   space).


---

## TurboHTTP Compliance

**Status:** ✅ Compliant

**Implementation Notes:**
TurboHTTP's `Http11ResponseDecoder` parses status-lines per §4. The decoder extracts HTTP-version, 3-digit status code, and optional reason-phrase. The reason-phrase is parsed but not used for application logic (as recommended by the RFC). Status codes are mapped to `HttpStatusCode` enum values.

**Key Components:**
- `Http11ResponseDecoder` — parses `HTTP-version SP status-code SP [reason-phrase]`
- `HttpStatusCode` — enum covering all standard status codes

**Compliance Details:**
- ✅ Status-line parsing: HTTP-version, status-code, reason-phrase
- ✅ 3-digit status-code extraction
- ✅ Reason-phrase ignored for logic (used for display only)
- ✅ Whitespace-delimited parsing with robustness

**Gaps:** None identified

**Test References:** `TurboHTTP.Tests.RFC9112`

---

**Navigation:** [[../RFC9112|RFC9112 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
