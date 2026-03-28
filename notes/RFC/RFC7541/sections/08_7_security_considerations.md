---
title: "7.  Security Considerations"
rfc_number: 7541
rfc_section: "7"
source_url: "https://www.rfc-editor.org/rfc/rfc7541"
description: "Section 7: Security Considerations — RFC 7541 — HPACK: Header Compression for HTTP/2"
tags: [RFC7541, HPACK, header-compression, HTTP/2, dynamic-table, static-table, Huffman-coding, indexed-representation, security_considerations]
---

# 7.  Security Considerations


   This section describes potential areas of security concern with
   HPACK:

   o  Use of compression as a length-based oracle for verifying guesses
      about secrets that are compressed into a shared compression
      context.

   o  Denial of service resulting from exhausting processing or memory
      capacity at a decoder.

## 7.1.  Probing Dynamic Table State

   HPACK reduces the length of header field encodings by exploiting the
   redundancy inherent in protocols like HTTP.  The ultimate goal of
   this is to reduce the amount of data that is required to send HTTP
   requests or responses.

   The compression context used to encode header fields can be probed by
   an attacker who can both define header fields to be encoded and
   transmitted and observe the length of those fields once they are
   encoded.  When an attacker can do both, they can adaptively modify
   requests in order to confirm guesses about the dynamic table state.
   If a guess is compressed into a shorter length, the attacker can
   observe the encoded length and infer that the guess was correct.

   This is possible even over the Transport Layer Security (TLS)
   protocol (see [TLS12]), because while TLS provides confidentiality
   protection for content, it only provides a limited amount of
   protection for the length of that content.

      Note: Padding schemes only provide limited protection against an
      attacker with these capabilities, potentially only forcing an
      increased number of guesses to learn the length associated with a
      given guess.  Padding schemes also work directly against
      compression by increasing the number of bits that are transmitted.




   Attacks like CRIME [CRIME] demonstrated the existence of these
   general attacker capabilities.  The specific attack exploited the
   fact that DEFLATE [DEFLATE] removes redundancy based on prefix
   matching.  This permitted the attacker to confirm guesses a character
   at a time, reducing an exponential-time attack into a linear-time
   attack.

### 7.1.1.  Applicability to HPACK and HTTP

   HPACK mitigates but does not completely prevent attacks modeled on
   CRIME [CRIME] by forcing a guess to match an entire header field
   value rather than individual characters.  Attackers can only learn
   whether a guess is correct or not, so they are reduced to brute-force
   guesses for the header field values.

   The viability of recovering specific header field values therefore
   depends on the entropy of values.  As a result, values with high
   entropy are unlikely to be recovered successfully.  However, values
   with low entropy remain vulnerable.

   Attacks of this nature are possible any time that two mutually
   distrustful entities control requests or responses that are placed
   onto a single HTTP/2 connection.  If the shared HPACK compressor
   permits one entity to add entries to the dynamic table and the other
   to access those entries, then the state of the table can be learned.

   Having requests or responses from mutually distrustful entities
   occurs when an intermediary either:

   o  sends requests from multiple clients on a single connection toward
      an origin server, or

   o  takes responses from multiple origin servers and places them on a
      shared connection toward a client.

   Web browsers also need to assume that requests made on the same
   connection by different web origins [ORIGIN] are made by mutually
   distrustful entities.

### 7.1.2.  Mitigation

   Users of HTTP that require confidentiality for header fields can use
   values with entropy sufficient to make guessing infeasible.  However,
   this is impractical as a general solution because it forces all users
   of HTTP to take steps to mitigate attacks.  It would impose new
   constraints on how HTTP is used.





   Rather than impose constraints on users of HTTP, an implementation of
   HPACK can instead constrain how compression is applied in order to
   limit the potential for dynamic table probing.

   An ideal solution segregates access to the dynamic table based on the
   entity that is constructing header fields.  Header field values that
   are added to the table are attributed to an entity, and only the
   entity that created a particular value can extract that value.

   To improve compression performance of this option, certain entries
   might be tagged as being public.  For example, a web browser might
   make the values of the Accept-Encoding header field available in all
   requests.

   An encoder without good knowledge of the provenance of header fields
   might instead introduce a penalty for a header field with many
   different values, such that a large number of attempts to guess a
   header field value results in the header field no longer being
   compared to the dynamic table entries in future messages, effectively
   preventing further guesses.

      Note: Simply removing entries corresponding to the header field
      from the dynamic table can be ineffectual if the attacker has a
      reliable way of causing values to be reinstalled.  For example, a
      request to load an image in a web browser typically includes the
      Cookie header field (a potentially highly valued target for this
      sort of attack), and web sites can easily force an image to be
      loaded, thereby refreshing the entry in the dynamic table.

   This response might be made inversely proportional to the length of
   the header field value.  Marking a header field as not using the
   dynamic table anymore might occur for shorter values more quickly or
   with higher probability than for longer values.

### 7.1.3.  Never-Indexed Literals

   Implementations can also choose to protect sensitive header fields by
   not compressing them and instead encoding their value as literals.

   Refusing to generate an indexed representation for a header field is
   only effective if compression is avoided on all hops.  The never-
   indexed literal (see Section 6.2.3) can be used to signal to
   intermediaries that a particular value was intentionally sent as a
   literal.







> **MUST NOT**: An intermediary MUST NOT re-encode a value that uses the never-
   indexed literal representation with another representation that would
   index it.  If HPACK is used for re-encoding, the never-indexed
> **MUST**: literal representation MUST be used.

   The choice to use a never-indexed literal representation for a header
   field depends on several factors.  Since HPACK doesn't protect
   against guessing an entire header field value, short or low-entropy
   values are more readily recovered by an adversary.  Therefore, an
   encoder might choose not to index values with low entropy.

   An encoder might also choose not to index values for header fields
   that are considered to be highly valuable or sensitive to recovery,
   such as the Cookie or Authorization header fields.

   On the contrary, an encoder might prefer indexing values for header
   fields that have little or no value if they were exposed.  For
   instance, a User-Agent header field does not commonly vary between
   requests and is sent to any server.  In that case, confirmation that
   a particular User-Agent value has been used provides little value.

   Note that these criteria for deciding to use a never-indexed literal
   representation will evolve over time as new attacks are discovered.

## 7.2.  Static Huffman Encoding

   There is no currently known attack against a static Huffman encoding.
   A study has shown that using a static Huffman encoding table created
   an information leakage; however, this same study concluded that an
   attacker could not take advantage of this information leakage to
   recover any meaningful amount of information (see [PETAL]).

## 7.3.  Memory Consumption

   An attacker can try to cause an endpoint to exhaust its memory.
   HPACK is designed to limit both the peak and state amounts of memory
   allocated by an endpoint.

   The amount of memory used by the compressor is limited by the
   protocol using HPACK through the definition of the maximum size of
   the dynamic table.  In HTTP/2, this value is controlled by the
   decoder through the setting parameter SETTINGS_HEADER_TABLE_SIZE (see
   Section 6.5.2 of [HTTP2]).  This limit takes into account both the
   size of the data stored in the dynamic table, plus a small allowance
   for overhead.






   A decoder can limit the amount of state memory used by setting an
   appropriate value for the maximum size of the dynamic table.  In
   HTTP/2, this is realized by setting an appropriate value for the
   SETTINGS_HEADER_TABLE_SIZE parameter.  An encoder can limit the
   amount of state memory it uses by signaling a lower dynamic table
   size than the decoder allows (see Section 6.3).

   The amount of temporary memory consumed by an encoder or decoder can
   be limited by processing header fields sequentially.  An
   implementation does not need to retain a complete list of header
   fields.  Note, however, that it might be necessary for an application
   to retain a complete header list for other reasons; even though HPACK
   does not force this to occur, application constraints might make this
   necessary.

## 7.4.  Implementation Limits

   An implementation of HPACK needs to ensure that large values for
   integers, long encoding for integers, or long string literals do not
   create security weaknesses.

   An implementation has to set a limit for the values it accepts for
   integers, as well as for the encoded length (see Section 5.1).  In
   the same way, it has to set a limit to the length it accepts for
   string literals (see Section 5.2).

---

**Navigation:** [[../RFC7541|RFC7541 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
