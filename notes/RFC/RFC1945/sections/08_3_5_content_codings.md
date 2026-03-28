---
title: "3.5.  Content Codings"
rfc_number: 1945
rfc_section: "3.5"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 3.5: Content Codings — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request, content_codings]
---

# 3.5.  Content Codings

## 3.5  Content Codings

   Content coding values are used to indicate an encoding transformation
   that has been applied to a resource. Content codings are primarily
   used to allow a document to be compressed or encrypted without losing
   the identity of its underlying media type. Typically, the resource is
   stored in this encoding and only decoded before rendering or
   analogous usage.


```abnf
       content-coding = "x-gzip" | "x-compress" | token
```


       Note: For future compatibility, HTTP/1.0 applications should
       consider "gzip" and "compress" to be equivalent to "x-gzip"
       and "x-compress", respectively.

   All content-coding values are case-insensitive. HTTP/1.0 uses
   content-coding values in the Content-Encoding (Section 10.3) header
   field. Although the value describes the content-coding, what is more
   important is that it indicates what decoding mechanism will be
   required to remove the encoding. Note that a single program may be
   capable of decoding multiple content-coding formats. Two values are
   defined by this specification:

   x-gzip
       An encoding format produced by the file compression program
       "gzip" (GNU zip) developed by Jean-loup Gailly. This format is
       typically a Lempel-Ziv coding (LZ77) with a 32 bit CRC.

   x-compress
       The encoding format produced by the file compression program
       "compress". This format is an adaptive Lempel-Ziv-Welch coding
       (LZW).

       Note: Use of program names for the identification of
       encoding formats is not desirable and should be discouraged
       for future encodings. Their use here is representative of
       historical practice, not good design.

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
