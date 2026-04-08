---
title: 6.  Message Body
rfc_number: 9112
rfc_section: '6'
source_url: 'https://www.rfc-editor.org/rfc/rfc9112'
description: 'Section 6: Message Body — RFC 9112 — HTTP/1.1'
tags:
  - RFC9112
  - HTTP/1.1
  - message-framing
  - chunked-encoding
  - connection-management
  - keep-alive
  - Host-header
  - pipelining
  - message_body
---

## 6.  Message Body

6.  Message Body

   The message body (if any) of an HTTP/1.1 message is used to carry
   content (Section 6.4 of [HTTP]) for the request or response.  The
   message body is identical to the content unless a transfer coding has
   been applied, as described in Section 6.1.


```abnf
     message-body = *OCTET
```


   The rules for determining when a message body is present in an
   HTTP/1.1 message differ for requests and responses.

   The presence of a message body in a request is signaled by a
   Content-Length or Transfer-Encoding header field.  Request message
   framing is independent of method semantics.

   The presence of a message body in a response, as detailed in
   Section 6.3, depends on both the request method to which it is
   responding and the response status code.  This corresponds to when
   response content is allowed by HTTP semantics (Section 6.4.1 of
   [HTTP]).

## 6.1  Transfer-Encoding

   The Transfer-Encoding header field lists the transfer coding names
   corresponding to the sequence of transfer codings that have been (or
   will be) applied to the content in order to form the message body.
   Transfer codings are defined in Section 7.


```abnf
     Transfer-Encoding = #transfer-coding
                          ; defined in [HTTP], Section 10.1.4
```


   Transfer-Encoding is analogous to the Content-Transfer-Encoding field
   of MIME, which was designed to enable safe transport of binary data
   over a 7-bit transport service ([RFC2045], Section 6).  However, safe
   transport has a different focus for an 8bit-clean transfer protocol.
   In HTTP's case, Transfer-Encoding is primarily intended to accurately
   delimit dynamically generated content.  It also serves to distinguish
   encodings that are only applied in transit from the encodings that
   are a characteristic of the selected representation.

> **MUST**: A recipient MUST be able to parse the chunked transfer coding
   (Section 7.1) because it plays a crucial role in framing messages
> **MUST NOT**: when the content size is not known in advance.  A sender MUST NOT
   apply the chunked transfer coding more than once to a message body
   (i.e., chunking an already chunked message is not allowed).  If any
   transfer coding other than chunked is applied to a request's content,
> **MUST**: the sender MUST apply chunked as the final transfer coding to ensure
   that the message is properly framed.  If any transfer coding other
> **MUST**: than chunked is applied to a response's content, the sender MUST
   either apply chunked as the final transfer coding or terminate the
   message by closing the connection.

   For example,

   Transfer-Encoding: gzip, chunked

   indicates that the content has been compressed using the gzip coding
   and then chunked using the chunked coding while forming the message
   body.

   Unlike Content-Encoding (Section 8.4.1 of [HTTP]), Transfer-Encoding
   is a property of the message, not of the representation.  Any
> **MAY**: recipient along the request/response chain MAY decode the received
   transfer coding(s) or apply additional transfer coding(s) to the
   message body, assuming that corresponding changes are made to the
   Transfer-Encoding field value.  Additional information about the
   encoding parameters can be provided by other header fields not
   defined by this specification.

> **MAY**: Transfer-Encoding MAY be sent in a response to a HEAD request or in a
   304 (Not Modified) response (Section 15.4.5 of [HTTP]) to a GET
   request, neither of which includes a message body, to indicate that
   the origin server would have applied a transfer coding to the message
   body if the request had been an unconditional GET.  This indication
   is not required, however, because any recipient on the response chain
   (including the origin server) can remove transfer codings when they
   are not needed.

> **MUST NOT**: A server MUST NOT send a Transfer-Encoding header field in any
   response with a status code of 1xx (Informational) or 204 (No
> **MUST NOT**: Content).  A server MUST NOT send a Transfer-Encoding header field in
   any 2xx (Successful) response to a CONNECT request (Section 9.3.6 of
   [HTTP]).

   A server that receives a request message with a transfer coding it
> **SHOULD**: does not understand SHOULD respond with 501 (Not Implemented).

   Transfer-Encoding was added in HTTP/1.1.  It is generally assumed
   that implementations advertising only HTTP/1.0 support will not
   understand how to process transfer-encoded content, and that an
   HTTP/1.0 message received with a Transfer-Encoding is likely to have
   been forwarded without proper handling of the chunked transfer coding
   in transit.

> **MUST NOT**: A client MUST NOT send a request containing Transfer-Encoding unless
   it knows the server will handle HTTP/1.1 requests (or later minor
   revisions); such knowledge might be in the form of specific user
   configuration or by remembering the version of a prior received
> **MUST NOT**: response.  A server MUST NOT send a response containing Transfer-
   Encoding unless the corresponding request indicates HTTP/1.1 (or
   later minor revisions).

   Early implementations of Transfer-Encoding would occasionally send
   both a chunked transfer coding for message framing and an estimated
   Content-Length header field for use by progress bars.  This is why
   Transfer-Encoding is defined as overriding Content-Length, as opposed
   to them being mutually incompatible.  Unfortunately, forwarding such
   a message can lead to vulnerabilities regarding request smuggling
   (Section 11.2) or response splitting (Section 11.1) attacks if any
   downstream recipient fails to parse the message according to this
   specification, particularly when a downstream recipient only
   implements HTTP/1.0.

> **MAY**: A server MAY reject a request that contains both Content-Length and
   Transfer-Encoding or process such a request in accordance with the
> **MUST**: Transfer-Encoding alone.  Regardless, the server MUST close the
   connection after responding to such a request to avoid the potential
   attacks.

   A server or client that receives an HTTP/1.0 message containing a
> **MUST**: Transfer-Encoding header field MUST treat the message as if the
   framing is faulty, even if a Content-Length is present, and close the
   connection after processing the message.  The message sender might
   have retained a portion of the message, in buffer, that could be
   misinterpreted by further use of the connection.

## 6.2  Content-Length

   When a message does not have a Transfer-Encoding header field, a
   Content-Length header field (Section 8.6 of [HTTP]) can provide the
   anticipated size, as a decimal number of octets, for potential
   content.  For messages that do include content, the Content-Length
   field value provides the framing information necessary for
   determining where the data (and message) ends.  For messages that do
   not include content, the Content-Length indicates the size of the
   selected representation (Section 8.6 of [HTTP]).

> **MUST NOT**: A sender MUST NOT send a Content-Length header field in any message
   that contains a Transfer-Encoding header field.

      |  *Note:* HTTP's use of Content-Length for message framing
      |  differs significantly from the same field's use in MIME, where
      |  it is an optional field used only within the "message/external-
      |  body" media-type.

## 6.3  Message Body Length

   The length of a message body is determined by one of the following
   (in order of precedence):

   1.  Any response to a HEAD request and any response with a 1xx
       (Informational), 204 (No Content), or 304 (Not Modified) status
       code is always terminated by the first empty line after the
       header fields, regardless of the header fields present in the
       message, and thus cannot contain a message body or trailer
       section.

   2.  Any 2xx (Successful) response to a CONNECT request implies that
       the connection will become a tunnel immediately after the empty
> **MUST**: line that concludes the header fields.  A client MUST ignore any
       Content-Length or Transfer-Encoding header fields received in
       such a message.

   3.  If a message is received with both a Transfer-Encoding and a
       Content-Length header field, the Transfer-Encoding overrides the
       Content-Length.  Such a message might indicate an attempt to
       perform request smuggling (Section 11.2) or response splitting
       (Section 11.1) and ought to be handled as an error.  An
> **MUST**: intermediary that chooses to forward the message MUST first
       remove the received Content-Length field and process the
       Transfer-Encoding (as described below) prior to forwarding the
       message downstream.

   4.  If a Transfer-Encoding header field is present and the chunked
       transfer coding (Section 7.1) is the final encoding, the message
       body length is determined by reading and decoding the chunked
       data until the transfer coding indicates the data is complete.

       If a Transfer-Encoding header field is present in a response and
       the chunked transfer coding is not the final encoding, the
       message body length is determined by reading the connection until
       it is closed by the server.

       If a Transfer-Encoding header field is present in a request and
       the chunked transfer coding is not the final encoding, the
       message body length cannot be determined reliably; the server
> **MUST**: MUST respond with the 400 (Bad Request) status code and then
       close the connection.

   5.  If a message is received without Transfer-Encoding and with an
       invalid Content-Length header field, then the message framing is
> **MUST**: invalid and the recipient MUST treat it as an unrecoverable
       error, unless the field value can be successfully parsed as a
       comma-separated list (Section 5.6.1 of [HTTP]), all values in the
       list are valid, and all values in the list are the same (in which
       case, the message is processed with that single value used as the
       Content-Length field value).  If the unrecoverable error is in a
> **MUST**: request message, the server MUST respond with a 400 (Bad Request)
       status code and then close the connection.  If it is in a
> **MUST**: response message received by a proxy, the proxy MUST close the
       connection to the server, discard the received response, and send
       a 502 (Bad Gateway) response to the client.  If it is in a
> **MUST**: response message received by a user agent, the user agent MUST
       close the connection to the server and discard the received
       response.

   6.  If a valid Content-Length header field is present without
       Transfer-Encoding, its decimal value defines the expected message
       body length in octets.  If the sender closes the connection or
       the recipient times out before the indicated number of octets are
> **MUST**: received, the recipient MUST consider the message to be
       incomplete and close the connection.

   7.  If this is a request message and none of the above are true, then
       the message body length is zero (no message body is present).

   8.  Otherwise, this is a response message without a declared message
       body length, so the message body length is determined by the
       number of octets received prior to the server closing the
       connection.

   Since there is no way to distinguish a successfully completed, close-
   delimited response message from a partially received message
> **SHOULD**: interrupted by network failure, a server SHOULD generate encoding or
   length-delimited messages whenever possible.  The close-delimiting
   feature exists primarily for backwards compatibility with HTTP/1.0.

      |  *Note:* Request messages are never close-delimited because they
      |  are always explicitly framed by length or transfer coding, with
      |  the absence of both implying the request ends immediately after
      |  the header section.

> **MAY**: A server MAY reject a request that contains a message body but not a
   Content-Length by responding with 411 (Length Required).

   Unless a transfer coding other than chunked has been applied, a
> **SHOULD**: client that sends a request containing a message body SHOULD use a
   valid Content-Length header field if the message body length is known
   in advance, rather than the chunked transfer coding, since some
   existing services respond to chunked with a 411 (Length Required)
   status code even though they understand the chunked transfer coding.
   This is typically because such services are implemented via a gateway
   that requires a content length in advance of being called, and the
   server is unable or unwilling to buffer the entire request before
   processing.

> **MUST**: A user agent that sends a request that contains a message body MUST
   send either a valid Content-Length header field or use the chunked
> **MUST NOT**: transfer coding.  A client MUST NOT use the chunked transfer coding
   unless it knows the server will handle HTTP/1.1 (or later) requests;
   such knowledge can be in the form of specific user configuration or
   by remembering the version of a prior received response.

   If the final response to the last request on a connection has been
   completely received and there remains additional data to read, a user
> **MAY**: agent MAY discard the remaining data or attempt to determine if that
   data belongs as part of the prior message body, which might be the
   case if the prior message's Content-Length value is incorrect.  A
> **MUST NOT**: client MUST NOT process, cache, or forward such extra data as a
   separate response, since such behavior would be vulnerable to cache
   poisoning.


---

## TurboHTTP Compliance

**Status:** ✅ Compliant

**Implementation Notes:**
TurboHTTP implements the full message body length determination algorithm from §6.3. The decoder supports Transfer-Encoding (chunked), Content-Length, and connection-close body framing. Transfer-Encoding takes precedence over Content-Length when both are present. The client generates Content-Length for known-size bodies and chunked encoding for streaming bodies.

**Key Components:**
- `Http11ResponseDecoder` — body length determination, chunked decoding, Content-Length framing
- `Http11RequestEncoder` — Content-Length and Transfer-Encoding generation
- `ChunkedDecodingStage` — Akka.Streams stage for chunked transfer decoding

**Compliance Details:**
- ✅ Transfer-Encoding overrides Content-Length (§6.3 rule 3)
- ✅ Chunked transfer coding decoding (§6.3 rule 4)
- ✅ Content-Length body framing (§6.3 rule 6)
- ✅ Connection-close body termination (§6.3 rule 8)
- ✅ HEAD/1xx/204/304 responses have no body (§6.3 rule 1)
- ✅ Invalid Content-Length detection
- ✅ Client sends Content-Length or chunked for request bodies

**Gaps:**
- CONNECT tunnel response handling (§6.3 rule 2) — CONNECT not supported

**Test References:** `TurboHTTP.Tests.RFC9112`

---

**Navigation:** [[../RFC9112|RFC9112 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
