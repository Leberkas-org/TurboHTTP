---
title: 2.  Message
rfc_number: 9112
rfc_section: '2'
source_url: 'https://www.rfc-editor.org/rfc/rfc9112'
description: 'Section 2: Message — RFC 9112 — HTTP/1.1'
tags:
  - RFC9112
  - HTTP/1.1
  - message-framing
  - chunked-encoding
  - connection-management
  - keep-alive
  - Host-header
  - pipelining
---

## 2.  Message

2.  Message

   HTTP/1.1 clients and servers communicate by sending messages.  See
   Section 3 of [HTTP] for the general terminology and core concepts of
   HTTP.

## 2.1  Message Format

   An HTTP/1.1 message consists of a start-line followed by a CRLF and a
   sequence of octets in a format similar to the Internet Message Format
   [RFC5322]: zero or more header field lines (collectively referred to
   as the "headers" or the "header section"), an empty line indicating
   the end of the header section, and an optional message body.


```abnf
     HTTP-message   = start-line CRLF
                      *( field-line CRLF )
                      CRLF
                      [ message-body ]
```


   A message can be either a request from client to server or a response
   from server to client.  Syntactically, the two types of messages
   differ only in the start-line, which is either a request-line (for
   requests) or a status-line (for responses), and in the algorithm for
   determining the length of the message body (Section 6).


```abnf
     start-line     = request-line / status-line
```


   In theory, a client could receive requests and a server could receive
   responses, distinguishing them by their different start-line formats.
   In practice, servers are implemented to only expect a request (a
   response is interpreted as an unknown or invalid request method), and
   clients are implemented to only expect a response.

   HTTP makes use of some protocol elements similar to the Multipurpose
   Internet Mail Extensions (MIME) [RFC2045].  See Appendix B for the
   differences between HTTP and MIME messages.

## 2.2  Message Parsing

   The normal procedure for parsing an HTTP message is to read the
   start-line into a structure, read each header field line into a hash
   table by field name until the empty line, and then use the parsed
   data to determine if a message body is expected.  If a message body
   has been indicated, then it is read as a stream until an amount of
   octets equal to the message body length is read or the connection is
   closed.

> **MUST**: A recipient MUST parse an HTTP message as a sequence of octets in an
   encoding that is a superset of US-ASCII [USASCII].  Parsing an HTTP
   message as a stream of Unicode characters, without regard for the
   specific encoding, creates security vulnerabilities due to the
   varying ways that string processing libraries handle invalid
   multibyte character sequences that contain the octet LF (%x0A).
   String-based parsers can only be safely used within protocol elements
   after the element has been extracted from the message, such as within
   a header field line value after message parsing has delineated the
   individual field lines.

   Although the line terminator for the start-line and fields is the
> **MAY**: sequence CRLF, a recipient MAY recognize a single LF as a line
   terminator and ignore any preceding CR.

> **MUST NOT**: A sender MUST NOT generate a bare CR (a CR character not immediately
   followed by LF) within any protocol elements other than the content.
> **MUST**: A recipient of such a bare CR MUST consider that element to be
   invalid or replace each bare CR with SP before processing the element
   or forwarding the message.

   Older HTTP/1.0 user agent implementations might send an extra CRLF
   after a POST request as a workaround for some early server
   applications that failed to read message body content that was not
> **MUST NOT**: terminated by a line-ending.  An HTTP/1.1 user agent MUST NOT preface
   or follow a request with an extra CRLF.  If terminating the request
> **MUST**: message body with a line-ending is desired, then the user agent MUST
   count the terminating CRLF octets as part of the message body length.

   In the interest of robustness, a server that is expecting to receive
> **SHOULD**: and parse a request-line SHOULD ignore at least one empty line (CRLF)
   received prior to the request-line.

> **MUST NOT**: A sender MUST NOT send whitespace between the start-line and the
   first header field.

   A recipient that receives whitespace between the start-line and the
> **MUST**: first header field MUST either reject the message as invalid or
   consume each whitespace-preceded line without further processing of
   it (i.e., ignore the entire line, along with any subsequent lines
   preceded by whitespace, until a properly formed header field is
   received or the header section is terminated).  Rejection or removal
   of invalid whitespace-preceded lines is necessary to prevent their
   misinterpretation by downstream recipients that might be vulnerable
   to request smuggling (Section 11.2) or response splitting
   (Section 11.1) attacks.

   When a server listening only for HTTP request messages, or processing
   what appears from the start-line to be an HTTP request message,
   receives a sequence of octets that does not match the HTTP-message
   grammar aside from the robustness exceptions listed above, the server
> **SHOULD**: SHOULD respond with a 400 (Bad Request) response and close the
   connection.

## 2.3  HTTP Version

   HTTP uses a "<major>.<minor>" numbering scheme to indicate versions
   of the protocol.  This specification defines version "1.1".
   Section 2.5 of [HTTP] specifies the semantics of HTTP version
   numbers.

   The version of an HTTP/1.x message is indicated by an HTTP-version
   field in the start-line.  HTTP-version is case-sensitive.


```abnf
     HTTP-version  = HTTP-name "/" DIGIT "." DIGIT
     HTTP-name     = %s"HTTP"
```


   When an HTTP/1.1 message is sent to an HTTP/1.0 recipient [HTTP/1.0]
   or a recipient whose version is unknown, the HTTP/1.1 message is
   constructed such that it can be interpreted as a valid HTTP/1.0
   message if all of the newer features are ignored.  This specification
   places recipient-version requirements on some new features so that a
   conformant sender will only use compatible features until it has
   determined, through configuration or the receipt of a message, that
   the recipient supports HTTP/1.1.

   Intermediaries that process HTTP messages (i.e., all intermediaries
> **MUST**: other than those acting as tunnels) MUST send their own HTTP-version
   in forwarded messages, unless it is purposefully downgraded as a
   workaround for an upstream issue.  In other words, an intermediary is
   not allowed to blindly forward the start-line without ensuring that
   the protocol version in that message matches a version to which that
   intermediary is conformant for both the receiving and sending of
   messages.  Forwarding an HTTP message without rewriting the HTTP-
   version might result in communication errors when downstream
   recipients use the message sender's version to determine what
   features are safe to use for later communication with that sender.

> **MAY**: A server MAY send an HTTP/1.0 response to an HTTP/1.1 request if it
   is known or suspected that the client incorrectly implements the HTTP
   specification and is incapable of correctly processing later version
   responses, such as when a client fails to parse the version number
   correctly or when an intermediary is known to blindly forward the
   HTTP-version even when it doesn't conform to the given minor version
> **SHOULD NOT**: of the protocol.  Such protocol downgrades SHOULD NOT be performed
   unless triggered by specific client attributes, such as when one or
   more of the request header fields (e.g., User-Agent) uniquely match
   the values sent by a client known to be in error.


---

## TurboHttp Compliance

**Status:** ✅ Compliant

**Implementation Notes:**
TurboHttp's `Http11ResponseDecoder` and `Http11RequestEncoder` implement HTTP/1.1 message framing per §2. Messages are parsed as octet sequences (not Unicode strings). The decoder handles start-line parsing, header field extraction, and body length determination. CRLF line terminators are required; bare LF tolerance is implemented for robustness. Bare CR characters within protocol elements are rejected.

**Key Components:**
- `Http11ResponseDecoder` — parses status-line, headers, and body from byte stream
- `Http11RequestEncoder` — generates request-line, headers, and body framing
- `Http11MessageParser` — low-level ABNF-compliant parsing utilities

**Compliance Details:**
- ✅ Parses as octet sequence (US-ASCII superset), not Unicode
- ✅ CRLF line termination enforced
- ✅ Bare CR handling (reject/replace)
- ✅ No extra CRLF before/after requests
- ✅ HTTP-version parsing and generation
- ✅ Whitespace between start-line and headers rejected

**Gaps:** None identified

**Test References:** `TurboHttp.Tests.RFC9112`

---

**Navigation:** [[../RFC9112|RFC9112 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
