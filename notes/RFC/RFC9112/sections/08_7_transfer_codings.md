---
title: 7.  Transfer Codings
rfc_number: 9112
rfc_section: '7'
source_url: 'https://www.rfc-editor.org/rfc/rfc9112'
description: 'Section 7: Transfer Codings — RFC 9112 — HTTP/1.1'
tags:
  - RFC9112
  - HTTP/1.1
  - message-framing
  - chunked-encoding
  - connection-management
  - keep-alive
  - Host-header
  - pipelining
  - transfer_codings
---

## 7.  Transfer Codings

7.  Transfer Codings

   Transfer coding names are used to indicate an encoding transformation
   that has been, can be, or might need to be applied to a message's
   content in order to ensure "safe transport" through the network.
   This differs from a content coding in that the transfer coding is a
   property of the message rather than a property of the representation
   that is being transferred.

   All transfer-coding names are case-insensitive and ought to be
   registered within the HTTP Transfer Coding registry, as defined in
   Section 7.3.  They are used in the Transfer-Encoding (Section 6.1)
   and TE (Section 10.1.4 of [HTTP]) header fields (the latter also
   defining the "transfer-coding" grammar).

## 7.1  Chunked Transfer Coding

   The chunked transfer coding wraps content in order to transfer it as
   a series of chunks, each with its own size indicator, followed by an
   OPTIONAL trailer section containing trailer fields.  Chunked enables
   content streams of unknown size to be transferred as a sequence of
   length-delimited buffers, which enables the sender to retain
   connection persistence and the recipient to know when it has received
   the entire message.


```abnf
     chunked-body   = *chunk
                      last-chunk
                      trailer-section
                      CRLF

     chunk          = chunk-size [ chunk-ext ] CRLF
                      chunk-data CRLF
     chunk-size     = 1*HEXDIG
     last-chunk     = 1*("0") [ chunk-ext ] CRLF

     chunk-data     = 1*OCTET ; a sequence of chunk-size octets
```


   The chunk-size field is a string of hex digits indicating the size of
   the chunk-data in octets.  The chunked transfer coding is complete
   when a chunk with a chunk-size of zero is received, possibly followed
   by a trailer section, and finally terminated by an empty line.

> **MUST**: A recipient MUST be able to parse and decode the chunked transfer
   coding.

   HTTP/1.1 does not define any means to limit the size of a chunked
   response such that an intermediary can be assured of buffering the
   entire response.  Additionally, very large chunk sizes may cause
   overflows or loss of precision if their values are not represented
> **MUST**: accurately in a receiving implementation.  Therefore, recipients MUST
   anticipate potentially large hexadecimal numerals and prevent parsing
   errors due to integer conversion overflows or precision loss due to
   integer representation.

   The chunked coding does not define any parameters.  Their presence
> **SHOULD**: SHOULD be treated as an error.

### 7.1.1  Chunk Extensions

   The chunked coding allows each chunk to include zero or more chunk
   extensions, immediately following the chunk-size, for the sake of
   supplying per-chunk metadata (such as a signature or hash), mid-
   message control information, or randomization of message body size.


```abnf
     chunk-ext      = *( BWS ";" BWS chunk-ext-name
                         [ BWS "=" BWS chunk-ext-val ] )

     chunk-ext-name = token
     chunk-ext-val  = token / quoted-string
```


   The chunked coding is specific to each connection and is likely to be
   removed or recoded by each recipient (including intermediaries)
   before any higher-level application would have a chance to inspect
   the extensions.  Hence, the use of chunk extensions is generally
   limited to specialized HTTP services such as "long polling" (where
   client and server can have shared expectations regarding the use of
   chunk extensions) or for padding within an end-to-end secured
   connection.

> **MUST**: A recipient MUST ignore unrecognized chunk extensions.  A server
   ought to limit the total length of chunk extensions received in a
   request to an amount reasonable for the services provided, in the
   same way that it applies length limitations and timeouts for other
   parts of a message, and generate an appropriate 4xx (Client Error)
   response if that amount is exceeded.

### 7.1.2  Chunked Trailer Section

   A trailer section allows the sender to include additional fields at
   the end of a chunked message in order to supply metadata that might
   be dynamically generated while the content is sent, such as a message
   integrity check, digital signature, or post-processing status.  The
   proper use and limitations of trailer fields are defined in
   Section 6.5 of [HTTP].


```abnf
     trailer-section   = *( field-line CRLF )
```


> **MAY**: A recipient that removes the chunked coding from a message MAY
   selectively retain or discard the received trailer fields.  A
> **MUST**: recipient that retains a received trailer field MUST either store/
   forward the trailer field separately from the received header fields
   or merge the received trailer field into the header section.  A
> **MUST NOT**: recipient MUST NOT merge a received trailer field into the header
   section unless its corresponding header field definition explicitly
   permits and instructs how the trailer field value can be safely
   merged.

### 7.1.3  Decoding Chunked

   A process for decoding the chunked transfer coding can be represented
   in pseudo-code as:

     length := 0
     read chunk-size, chunk-ext (if any), and CRLF
     while (chunk-size > 0) {
        read chunk-data and CRLF
        append chunk-data to content
        length := length + chunk-size
        read chunk-size, chunk-ext (if any), and CRLF
     }
     read trailer field
     while (trailer field is not empty) {
        if (trailer fields are stored/forwarded separately) {
            append trailer field to existing trailer fields
        }
        else if (trailer field is understood and defined as mergeable) {
            merge trailer field with existing header fields
        }
        else {
            discard trailer field
        }
        read trailer field
     }
     Content-Length := length
     Remove "chunked" from Transfer-Encoding

## 7.2  Transfer Codings for Compression

   The following transfer coding names for compression are defined by
   the same algorithm as their corresponding content coding:

   compress (and x-compress)
      See Section 8.4.1.1 of [HTTP].

   deflate
      See Section 8.4.1.2 of [HTTP].

   gzip (and x-gzip)
      See Section 8.4.1.3 of [HTTP].

   The compression codings do not define any parameters.  The presence
> **SHOULD**: of parameters with any of these compression codings SHOULD be treated
   as an error.

## 7.3  Transfer Coding Registry

   The "HTTP Transfer Coding Registry" defines the namespace for
   transfer coding names.  It is maintained at
   <https://www.iana.org/assignments/http-parameters>.

> **MUST**: Registrations MUST include the following fields:

   *  Name

   *  Description

   *  Pointer to specification text

> **MUST NOT**: Names of transfer codings MUST NOT overlap with names of content
   codings (Section 8.4.1 of [HTTP]) unless the encoding transformation
   is identical, as is the case for the compression codings defined in
   Section 7.2.

   The TE header field (Section 10.1.4 of [HTTP]) uses a pseudo-
   parameter named "q" as the rank value when multiple transfer codings
> **SHOULD NOT**: are acceptable.  Future registrations of transfer codings SHOULD NOT
   define parameters called "q" (case-insensitively) in order to avoid
   ambiguities.

   Values to be added to this namespace require IETF Review (see
> **MUST**: Section 4.8 of [RFC8126]) and MUST conform to the purpose of transfer
   coding defined in this specification.

   Use of program names for the identification of encoding formats is
   not desirable and is discouraged for future encodings.

## 7.4  Negotiating Transfer Codings

   The TE field (Section 10.1.4 of [HTTP]) is used in HTTP/1.1 to
   indicate what transfer codings, besides chunked, the client is
   willing to accept in the response and whether the client is willing
   to preserve trailer fields in a chunked transfer coding.

> **MUST NOT**: A client MUST NOT send the chunked transfer coding name in TE;
   chunked is always acceptable for HTTP/1.1 recipients.

   Three examples of TE use are below.

   TE: deflate
   TE:
   TE: trailers, deflate;q=0.5

> **MAY**: When multiple transfer codings are acceptable, the client MAY rank
   the codings by preference using a case-insensitive "q" parameter
   (similar to the qvalues used in content negotiation fields; see
   Section 12.4.2 of [HTTP]).  The rank value is a real number in the
   range 0 through 1, where 0.001 is the least preferred and 1 is the
   most preferred; a value of 0 means "not acceptable".

   If the TE field value is empty or if no TE field is present, the only
   acceptable transfer coding is chunked.  A message with no transfer
   coding is always acceptable.

   The keyword "trailers" indicates that the sender will not discard
   trailer fields, as described in Section 6.5 of [HTTP].

   Since the TE header field only applies to the immediate connection, a
> **MUST**: sender of TE MUST also send a "TE" connection option within the
   Connection header field (Section 7.6.1 of [HTTP]) in order to prevent
   the TE header field from being forwarded by intermediaries that do
   not support its semantics.


---

## TurboHTTP Compliance

**Status:** ✅ Compliant

**Implementation Notes:**
TurboHTTP fully supports chunked transfer coding for both decoding responses and encoding requests. The `ChunkedDecodingStage` handles chunk-size parsing, chunk-data extraction, last-chunk detection, and trailer section processing. Chunk extensions are parsed and ignored per spec. Compression transfer codings (gzip, deflate) are handled by the separate `DecompressionStage`.

**Key Components:**
- `ChunkedDecodingStage` — Akka.Streams stage for chunked transfer decoding
- `Http11ResponseDecoder` — Transfer-Encoding detection and routing
- `Http11RequestEncoder` — chunked encoding for streaming request bodies
- `DecompressionStage` — handles gzip/deflate transfer codings

**Compliance Details:**
- ✅ Chunked transfer coding parsing and decoding (§7.1)
- ✅ Large chunk-size handling (overflow protection)
- ✅ Chunk extensions parsed and ignored (§7.1.1)
- ✅ Trailer section handling (§7.1.2)
- ✅ Decoding algorithm per §7.1.3
- ✅ Gzip and deflate compression codings (§7.2)
- ✅ TE header not sent with "chunked" (§7.4)

**Gaps:**
- Compress/x-compress (LZW) not supported
- Chunk extension parameters not treated as error (SHOULD)

**Test References:** `TurboHTTP.Tests.RFC9112`

---

**Navigation:** [[../RFC9112|RFC9112 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
