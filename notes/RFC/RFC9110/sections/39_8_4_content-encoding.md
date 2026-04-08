---
title: "8.4.  Content-Encoding"
rfc_number: 9110
rfc_section: "8.4"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 8.4: Content-Encoding — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, content-encoding]
---

## 8.4.  Content-Encoding

## 8.4  Content-Encoding

   The "Content-Encoding" header field indicates what content codings
   have been applied to the representation, beyond those inherent in the
   media type, and thus what decoding mechanisms have to be applied in
   order to obtain data in the media type referenced by the Content-Type
   header field.  Content-Encoding is primarily used to allow a
   representation's data to be compressed without losing the identity of
   its underlying media type.


```abnf
     Content-Encoding = #content-coding
```


   An example of its use is

   Content-Encoding: gzip

   If one or more encodings have been applied to a representation, the
> **MUST**: sender that applied the encodings MUST generate a Content-Encoding
   header field that lists the content codings in the order in which
   they were applied.  Note that the coding named "identity" is reserved
> **SHOULD NOT**: for its special role in Accept-Encoding and thus SHOULD NOT be
   included.

   Additional information about the encoding parameters can be provided
   by other header fields not defined by this specification.

   Unlike Transfer-Encoding (Section 6.1 of [HTTP/1.1]), the codings
   listed in Content-Encoding are a characteristic of the
   representation; the representation is defined in terms of the coded
   form, and all other metadata about the representation is about the
   coded form unless otherwise noted in the metadata definition.
   Typically, the representation is only decoded just prior to rendering
   or analogous usage.

   If the media type includes an inherent encoding, such as a data
   format that is always compressed, then that encoding would not be
   restated in Content-Encoding even if it happens to be the same
   algorithm as one of the content codings.  Such a content coding would
   only be listed if, for some bizarre reason, it is applied a second
   time to form the representation.  Likewise, an origin server might
   choose to publish the same data as multiple representations that
   differ only in whether the coding is defined as part of Content-Type
   or Content-Encoding, since some user agents will behave differently
   in their handling of each response (e.g., open a "Save as ..." dialog
   instead of automatic decompression and rendering of content).

> **MAY**: An origin server MAY respond with a status code of 415 (Unsupported
   Media Type) if a representation in the request message has a content
   coding that is not acceptable.

### 8.4.1  Content Codings

   Content coding values indicate an encoding transformation that has
   been or can be applied to a representation.  Content codings are
   primarily used to allow a representation to be compressed or
   otherwise usefully transformed without losing the identity of its
   underlying media type and without loss of information.  Frequently,
   the representation is stored in coded form, transmitted directly, and
   only decoded by the final recipient.


```abnf
     content-coding   = token
```


   All content codings are case-insensitive and ought to be registered
   within the "HTTP Content Coding Registry", as described in
   Section 16.6

   Content-coding values are used in the Accept-Encoding
   (Section 12.5.3) and Content-Encoding (Section 8.4) header fields.

#### 8.4.1.1  Compress Coding

   The "compress" coding is an adaptive Lempel-Ziv-Welch (LZW) coding
   [Welch] that is commonly produced by the UNIX file compression
> **SHOULD**: program "compress".  A recipient SHOULD consider "x-compress" to be
   equivalent to "compress".

#### 8.4.1.2  Deflate Coding

   The "deflate" coding is a "zlib" data format [RFC1950] containing a
   "deflate" compressed data stream [RFC1951] that uses a combination of
   the Lempel-Ziv (LZ77) compression algorithm and Huffman coding.

      |  *Note:* Some non-conformant implementations send the "deflate"
      |  compressed data without the zlib wrapper.

#### 8.4.1.3  Gzip Coding

   The "gzip" coding is an LZ77 coding with a 32-bit Cyclic Redundancy
   Check (CRC) that is commonly produced by the gzip file compression
> **SHOULD**: program [RFC1952].  A recipient SHOULD consider "x-gzip" to be
   equivalent to "gzip".

---

## TurboHTTP Compliance

**Status**: ✅ Compliant

### Implementation Notes
- **`DecompressionStage.cs`** — Decodes gzip, deflate, and br (Brotli) content encodings; processes Content-Encoding header to determine decoding chain order
- **`ContentEncodingHandler.cs`** — Parses Content-Encoding header; applies decodings in reverse order per §8.4
- **`AcceptEncodingBuilder.cs`** — Generates Accept-Encoding request header advertising supported codings (gzip, deflate, br)

### Test References
- `TurboHTTP.Tests/RFC9110/39_ContentEncodingTests.cs` — gzip/deflate/br decoding, multi-layer encoding, x-gzip equivalence

### Known Gaps
- ❌ Compress (LZW) — Not supported; x-compress/compress coding not implemented
- ⚠️ Identity coding — Correctly excluded from Content-Encoding but not explicitly validated on receipt

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
