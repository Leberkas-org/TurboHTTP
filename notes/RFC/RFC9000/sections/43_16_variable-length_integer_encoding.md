---
title: "16.  Variable-Length Integer Encoding"
rfc_number: 9000
rfc_section: "16"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 16: Variable-Length Integer Encoding — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, variable-length_integer_encoding]
---

# 16.  Variable-Length Integer Encoding


   QUIC packets and frames commonly use a variable-length encoding for
   non-negative integer values.  This encoding ensures that smaller
   integer values need fewer bytes to encode.

   The QUIC variable-length integer encoding reserves the two most
   significant bits of the first byte to encode the base-2 logarithm of
   the integer encoding length in bytes.  The integer value is encoded
   on the remaining bits, in network byte order.

   This means that integers are encoded on 1, 2, 4, or 8 bytes and can
   encode 6-, 14-, 30-, or 62-bit values, respectively.  Table 4
   summarizes the encoding properties.

          +======+========+=============+=======================+
          | 2MSB | Length | Usable Bits | Range                 |
          +======+========+=============+=======================+
          | 00   | 1      | 6           | 0-63                  |
          +------+--------+-------------+-----------------------+
          | 01   | 2      | 14          | 0-16383               |
          +------+--------+-------------+-----------------------+
          | 10   | 4      | 30          | 0-1073741823          |
          +------+--------+-------------+-----------------------+
          | 11   | 8      | 62          | 0-4611686018427387903 |
          +------+--------+-------------+-----------------------+

                   Table 4: Summary of Integer Encodings

   An example of a decoding algorithm and sample encodings are shown in
   Appendix A.1.

   Values do not need to be encoded on the minimum number of bytes
   necessary, with the sole exception of the Frame Type field; see
   Section 12.4.

   Versions (Section 15), packet numbers sent in the header
   (Section 17.1), and the length of connection IDs in long header
   packets (Section 17.2) are described using integers but do not use
   this encoding.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
