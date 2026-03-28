---
title: "Appendix C.  Sample Single-Pass Encoding Algorithm"
rfc_number: 9204
rfc_section: "Appendix C"
source_url: "https://www.rfc-editor.org/rfc/rfc9204"
description: "Appendix C: Sample Single-Pass Encoding Algorithm — RFC 9204 — QPACK: Field Compression for HTTP/3"
tags: [RFC9204, QPACK, header-compression, HTTP/3, dynamic-table, static-table, blocking-references, encoder-instructions, decoder-instructions, sample_single-pass_encoding_algorithm]
---

## Appendix C.  Sample Single-Pass Encoding Algorithm

Appendix C.  Sample Single-Pass Encoding Algorithm

   Pseudocode for single-pass encoding, excluding handling of
   duplicates, non-blocking mode, available encoder stream flow control
   and reference tracking.

   # Helper functions:
   # ====
   # Encode an integer with the specified prefix and length
   encodeInteger(buffer, prefix, value, prefixLength)

   # Encode a dynamic table insert instruction with optional static
   # or dynamic name index (but not both)
   encodeInsert(buffer, staticNameIndex, dynamicNameIndex, fieldLine)

   # Encode a static index reference
   encodeStaticIndexReference(buffer, staticIndex)

   # Encode a dynamic index reference relative to Base
   encodeDynamicIndexReference(buffer, dynamicIndex, base)

   # Encode a literal with an optional static name index
   encodeLiteral(buffer, staticNameIndex, fieldLine)

   # Encode a literal with a dynamic name index relative to Base
   encodeDynamicLiteral(buffer, dynamicNameIndex, base, fieldLine)

   # Encoding Algorithm
   # ====

```abnf
   base = dynamicTable.getInsertCount()
   requiredInsertCount = 0
```

   for line in fieldLines:

```abnf
     staticIndex = staticTable.findIndex(line)
```

     if staticIndex is not None:
       encodeStaticIndexReference(streamBuffer, staticIndex)
       continue


```abnf
     dynamicIndex = dynamicTable.findIndex(line)
```

     if dynamicIndex is None:
       # No matching entry.  Either insert+index or encode literal

```abnf
       staticNameIndex = staticTable.findName(line.name)
       if staticNameIndex is None:
          dynamicNameIndex = dynamicTable.findName(line.name)
```


       if shouldIndex(line) and dynamicTable.canIndex(line):
         encodeInsert(encoderBuffer, staticNameIndex,
                      dynamicNameIndex, line)

```abnf
         dynamicIndex = dynamicTable.add(line)
```


     if dynamicIndex is None:
       # Could not index it, literal
       if dynamicNameIndex is not None:
         # Encode literal with dynamic name, possibly above Base
         encodeDynamicLiteral(streamBuffer, dynamicNameIndex,
                              base, line)

```abnf
         requiredInsertCount = max(requiredInsertCount,
                                   dynamicNameIndex)
       else:
         # Encodes a literal with a static name or literal name
         encodeLiteral(streamBuffer, staticNameIndex, line)
```

     else:
       # Dynamic index reference
       assert(dynamicIndex is not None)

```abnf
       requiredInsertCount = max(requiredInsertCount, dynamicIndex)
       # Encode dynamicIndex, possibly above Base
       encodeDynamicIndexReference(streamBuffer, dynamicIndex, base)
```


   # encode the prefix
   if requiredInsertCount == 0:
     encodeInteger(prefixBuffer, 0x00, 0, 8)
     encodeInteger(prefixBuffer, 0x00, 0, 7)
   else:

```abnf
     wireRIC = (
       requiredInsertCount
       % (2 * getMaxEntries(maxTableCapacity))
```

     ) + 1;
     encodeInteger(prefixBuffer, 0x00, wireRIC, 8)
     if base >= requiredInsertCount:
       encodeInteger(prefixBuffer, 0x00,
                     base - requiredInsertCount, 7)
     else:
       encodeInteger(prefixBuffer, 0x80,
                     requiredInsertCount - base - 1, 7)

   return encoderBuffer, prefixBuffer + streamBuffer

Acknowledgments

   The IETF QUIC Working Group received an enormous amount of support
   from many people.

   The compression design team did substantial work exploring the
   problem space and influencing the initial draft version of this
   document.  The contributions of design team members Roberto Peon,
   Martin Thomson, and Dmitri Tikhonov are gratefully acknowledged.

   The following people also provided substantial contributions to this
   document:

   *  Bence Beky
   *  Alessandro Ghedini
   *  Ryan Hamilton
   *  Robin Marx
   *  Patrick McManus
   *  奥 一穂 (Kazuho Oku)
   *  Lucas Pardue
   *  Biren Roy
   *  Ian Swett

   This document draws heavily on the text of [RFC7541].  The indirect
   input of those authors is also gratefully acknowledged.

   Buck Krasic's contribution was supported by Google during his
   employment there.

   A portion of Mike Bishop's contribution was supported by Microsoft
   during his employment there.

---

**Navigation:** [[../RFC9204|RFC9204 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
