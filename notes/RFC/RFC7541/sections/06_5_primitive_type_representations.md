---
title: "5.  Primitive Type Representations"
rfc_number: 7541
rfc_section: "5"
source_url: "https://www.rfc-editor.org/rfc/rfc7541"
description: "Section 5: Primitive Type Representations — RFC 7541 — HPACK: Header Compression for HTTP/2"
tags: [RFC7541, HPACK, header-compression, HTTP/2, dynamic-table, static-table, Huffman-coding, indexed-representation, primitive_type_representations]
---

# 5.  Primitive Type Representations


   HPACK encoding uses two primitive types: unsigned variable-length
   integers and strings of octets.

## 5.1.  Integer Representation

   Integers are used to represent name indexes, header field indexes, or
   string lengths.  An integer representation can start anywhere within
   an octet.  To allow for optimized processing, an integer
   representation always finishes at the end of an octet.

   An integer is represented in two parts: a prefix that fills the
   current octet and an optional list of octets that are used if the
   integer value does not fit within the prefix.  The number of bits of
   the prefix (called N) is a parameter of the integer representation.

   If the integer value is small enough, i.e., strictly less than 2^N-1,
   it is encoded within the N-bit prefix.







     0   1   2   3   4   5   6   7
   +---+---+---+---+---+---+---+---+
   | ? | ? | ? |       Value       |
   +---+---+---+-------------------+

    Figure 2: Integer Value Encoded within the Prefix (Shown for N = 5)

   Otherwise, all the bits of the prefix are set to 1, and the value,
   decreased by 2^N-1, is encoded using a list of one or more octets.
   The most significant bit of each octet is used as a continuation
   flag: its value is set to 1 except for the last octet in the list.
   The remaining bits of the octets are used to encode the decreased
   value.

     0   1   2   3   4   5   6   7
   +---+---+---+---+---+---+---+---+
   | ? | ? | ? | 1   1   1   1   1 |
   +---+---+---+-------------------+
   | 1 |    Value-(2^N-1) LSB      |
   +---+---------------------------+
                  ...
   +---+---------------------------+
   | 0 |    Value-(2^N-1) MSB      |
   +---+---------------------------+

    Figure 3: Integer Value Encoded after the Prefix (Shown for N = 5)

   Decoding the integer value from the list of octets starts by
   reversing the order of the octets in the list.  Then, for each octet,
   its most significant bit is removed.  The remaining bits of the
   octets are concatenated, and the resulting value is increased by
   2^N-1 to obtain the integer value.

   The prefix size, N, is always between 1 and 8 bits.  An integer
   starting at an octet boundary will have an 8-bit prefix.

   Pseudocode to represent an integer I is as follows:

   if I < 2^N - 1, encode I on N bits
   else
       encode (2^N - 1) on N bits

```abnf
       I = I - (2^N - 1)
       while I >= 128
            encode (I % 128 + 128) on 8 bits
            I = I / 128
       encode I on 8 bits
```






   Pseudocode to decode an integer I is as follows:

   decode I from the next N bits
   if I < 2^N - 1, return I
   else

```abnf
       M = 0
       repeat
           B = next octet
           I = I + (B & 127) * 2^M
           M = M + 7
       while B & 128 == 128
       return I
```


   Examples illustrating the encoding of integers are available in
   Appendix C.1.

   This integer representation allows for values of indefinite size.  It
   is also possible for an encoder to send a large number of zero
   values, which can waste octets and could be used to overflow integer
   values.  Integer encodings that exceed implementation limits -- in
> **MUST**: value or octet length -- MUST be treated as decoding errors.
   Different limits can be set for each of the different uses of
   integers, based on implementation constraints.

## 5.2.  String Literal Representation

   Header field names and header field values can be represented as
   string literals.  A string literal is encoded as a sequence of
   octets, either by directly encoding the string literal's octets or by
   using a Huffman code (see [HUFFMAN]).

     0   1   2   3   4   5   6   7
   +---+---+---+---+---+---+---+---+
   | H |    String Length (7+)     |
   +---+---------------------------+
   |  String Data (Length octets)  |
   +-------------------------------+

                  Figure 4: String Literal Representation

   A string literal representation contains the following fields:

   H: A one-bit flag, H, indicating whether or not the octets of the
      string are Huffman encoded.

   String Length:  The number of octets used to encode the string
      literal, encoded as an integer with a 7-bit prefix (see
      Section 5.1).



   String Data:  The encoded data of the string literal.  If H is '0',
      then the encoded data is the raw octets of the string literal.  If
      H is '1', then the encoded data is the Huffman encoding of the
      string literal.

   String literals that use Huffman encoding are encoded with the
   Huffman code defined in Appendix B (see examples for requests in
   Appendix C.4 and for responses in Appendix C.6).  The encoded data is
   the bitwise concatenation of the codes corresponding to each octet of
   the string literal.

   As the Huffman-encoded data doesn't always end at an octet boundary,
   some padding is inserted after it, up to the next octet boundary.  To
   prevent this padding from being misinterpreted as part of the string
   literal, the most significant bits of the code corresponding to the
   EOS (end-of-string) symbol are used.

   Upon decoding, an incomplete code at the end of the encoded data is
   to be considered as padding and discarded.  A padding strictly longer
> **MUST**: than 7 bits MUST be treated as a decoding error.  A padding not
   corresponding to the most significant bits of the code for the EOS
> **MUST**: symbol MUST be treated as a decoding error.  A Huffman-encoded string
   literal containing the EOS symbol MUST be treated as a decoding
   error.

---

**Navigation:** [[../RFC7541|RFC7541 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
