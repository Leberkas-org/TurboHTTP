---
title: "14.1.  Range Units"
rfc_number: 9110
rfc_section: "14.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 14.1: Range Units — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, range_units]
---

## 14.1.  Range Units

14.  Range Requests

   Clients often encounter interrupted data transfers as a result of
   canceled requests or dropped connections.  When a client has stored a
   partial representation, it is desirable to request the remainder of
   that representation in a subsequent request rather than transfer the
   entire representation.  Likewise, devices with limited local storage
   might benefit from being able to request only a subset of a larger
   representation, such as a single page of a very large document, or
   the dimensions of an embedded image.

   Range requests are an OPTIONAL feature of HTTP, designed so that
   recipients not implementing this feature (or not supporting it for
   the target resource) can respond as if it is a normal GET request
   without impacting interoperability.  Partial responses are indicated
   by a distinct status code to not be mistaken for full responses by
   caches that might not implement the feature.

## 14.1  Range Units

   Representation data can be partitioned into subranges when there are
   addressable structural units inherent to that data's content coding
   or media type.  For example, octet (a.k.a. byte) boundaries are a
   structural unit common to all representation data, allowing
   partitions of the data to be identified as a range of bytes at some
   offset from the start or end of that data.

   This general notion of a "range unit" is used in the Accept-Ranges
   (Section 14.3) response header field to advertise support for range
   requests, the Range (Section 14.2) request header field to delineate
   the parts of a representation that are requested, and the
   Content-Range (Section 14.4) header field to describe which part of a
   representation is being transferred.


```abnf
     range-unit       = token
```


   All range unit names are case-insensitive and ought to be registered
   within the "HTTP Range Unit Registry", as defined in Section 16.5.1.

   Range units are intended to be extensible, as described in
   Section 16.5.

### 14.1.1  Range Specifiers

   Ranges are expressed in terms of a range unit paired with a set of
   range specifiers.  The range unit name determines what kinds of
   range-spec are applicable to its own specifiers.  Hence, the
   following grammar is generic: each range unit is expected to specify
   requirements on when int-range, suffix-range, and other-range are
   allowed.

   A range request can specify a single range or a set of ranges within
   a single representation.


```abnf
     ranges-specifier = range-unit "=" range-set
     range-set        = 1#range-spec
     range-spec       = int-range
                      / suffix-range
                      / other-range
```


   An int-range is a range expressed as two non-negative integers or as
   one non-negative integer through to the end of the representation
   data.  The range unit specifies what the integers mean (e.g., they
   might indicate unit offsets from the beginning, inclusive numbered
   parts, etc.).


```abnf
     int-range     = first-pos "-" [ last-pos ]
     first-pos     = 1*DIGIT
     last-pos      = 1*DIGIT
```


   An int-range is invalid if the last-pos value is present and less
   than the first-pos.

   A suffix-range is a range expressed as a suffix of the representation
   data with the provided non-negative integer maximum length (in range
   units).  In other words, the last N units of the representation data.


```abnf
     suffix-range  = "-" suffix-length
     suffix-length = 1*DIGIT
```


   To provide for extensibility, the other-range rule is a mostly
   unconstrained grammar that allows application-specific or future
   range units to define additional range specifiers.


```abnf
     other-range   = 1*( %x21-2B / %x2D-7E )
                   ; 1*(VCHAR excluding comma)
```


   A ranges-specifier is invalid if it contains any range-spec that is
   invalid or undefined for the indicated range-unit.

   A valid ranges-specifier is "satisfiable" if it contains at least one
   range-spec that is satisfiable, as defined by the indicated
   range-unit.  Otherwise, the ranges-specifier is "unsatisfiable".

### 14.1.2  Byte Ranges

   The "bytes" range unit is used to express subranges of a
   representation data's octet sequence.  Each byte range is expressed
   as an integer range at some offset, relative to either the beginning
   (int-range) or end (suffix-range) of the representation data.  Byte
   ranges do not use the other-range specifier.

   The first-pos value in a bytes int-range gives the offset of the
   first byte in a range.  The last-pos value gives the offset of the
   last byte in the range; that is, the byte positions specified are
   inclusive.  Byte offsets start at zero.

   If the representation data has a content coding applied, each byte
   range is calculated with respect to the encoded sequence of bytes,
   not the sequence of underlying bytes that would be obtained after
   decoding.

   Examples of bytes range specifiers:

   *  The first 500 bytes (byte offsets 0-499, inclusive):

           bytes=0-499

   *  The second 500 bytes (byte offsets 500-999, inclusive):

           bytes=500-999

   A client can limit the number of bytes requested without knowing the
   size of the selected representation.  If the last-pos value is
   absent, or if the value is greater than or equal to the current
   length of the representation data, the byte range is interpreted as
   the remainder of the representation (i.e., the server replaces the
   value of last-pos with a value that is one less than the current
   length of the selected representation).

   A client can refer to the last N bytes (N > 0) of the selected
   representation using a suffix-range.  If the selected representation
   is shorter than the specified suffix-length, the entire
   representation is used.

   Additional examples, assuming a representation of length 10000:

   *  The final 500 bytes (byte offsets 9500-9999, inclusive):

           bytes=-500

      Or:

           bytes=9500-

   *  The first and last bytes only (bytes 0 and 9999):

           bytes=0-0,-1

   *  The first, middle, and last 1000 bytes:


```abnf
           bytes= 0-999, 4500-5499, -1000
```


   *  Other valid (but not canonical) specifications of the second 500
      bytes (byte offsets 500-999, inclusive):

           bytes=500-600,601-999
           bytes=500-700,601-999

   For a GET request, a valid bytes range-spec is satisfiable if it is
   either:

   *  an int-range with a first-pos that is less than the current length
      of the selected representation or

   *  a suffix-range with a non-zero suffix-length.

   When a selected representation has zero length, the only satisfiable
   form of range-spec in a GET request is a suffix-range with a non-zero
   suffix-length.

   In the byte-range syntax, first-pos, last-pos, and suffix-length are
   expressed as decimal number of octets.  Since there is no predefined
> **MUST**: limit to the length of content, recipients MUST anticipate
   potentially large decimal numerals and prevent parsing errors due to
   integer conversion overflows.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
