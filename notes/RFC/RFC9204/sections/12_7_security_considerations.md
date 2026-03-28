---
title: "7.  Security Considerations"
rfc_number: 9204
rfc_section: "7"
source_url: "https://www.rfc-editor.org/rfc/rfc9204"
description: "Section 7: Security Considerations — RFC 9204 — QPACK: Field Compression for HTTP/3"
tags: [RFC9204, QPACK, header-compression, HTTP/3, dynamic-table, static-table, blocking-references, encoder-instructions, decoder-instructions, security_considerations]
---

## 7.  Security Considerations

7.  Security Considerations

   This section describes potential areas of security concern with
   QPACK:

   *  Use of compression as a length-based oracle for verifying guesses
      about secrets that are compressed into a shared compression
      context.

   *  Denial of service resulting from exhausting processing or memory
      capacity at a decoder.

## 7.1  Probing Dynamic Table State

   QPACK reduces the encoded size of field sections by exploiting the
   redundancy inherent in protocols like HTTP.  The ultimate goal of
   this is to reduce the amount of data that is required to send HTTP
   requests or responses.

   The compression context used to encode header and trailer fields can
   be probed by an attacker who can both define fields to be encoded and
   transmitted and observe the length of those fields once they are
   encoded.  When an attacker can do both, they can adaptively modify
   requests in order to confirm guesses about the dynamic table state.
   If a guess is compressed into a shorter length, the attacker can
   observe the encoded length and infer that the guess was correct.

   This is possible even over the Transport Layer Security Protocol
   ([TLS]) and the QUIC Transport Protocol ([QUIC-TRANSPORT]), because
   while TLS and QUIC provide confidentiality protection for content,
   they only provide a limited amount of protection for the length of
   that content.

      |  Note: Padding schemes only provide limited protection against
      |  an attacker with these capabilities, potentially only forcing
      |  an increased number of guesses to learn the length associated
      |  with a given guess.  Padding schemes also work directly against
      |  compression by increasing the number of bits that are
      |  transmitted.

   Attacks like CRIME ([CRIME]) demonstrated the existence of these
   general attacker capabilities.  The specific attack exploited the
   fact that DEFLATE ([RFC1951]) removes redundancy based on prefix
   matching.  This permitted the attacker to confirm guesses a character
   at a time, reducing an exponential-time attack into a linear-time
   attack.

### 7.1.1  Applicability to QPACK and HTTP

   QPACK mitigates, but does not completely prevent, attacks modeled on
   CRIME ([CRIME]) by forcing a guess to match an entire field line
   rather than individual characters.  An attacker can only learn
   whether a guess is correct or not, so the attacker is reduced to a
   brute-force guess for the field values associated with a given field
   name.

   Therefore, the viability of recovering specific field values depends
   on the entropy of values.  As a result, values with high entropy are
   unlikely to be recovered successfully.  However, values with low
   entropy remain vulnerable.

   Attacks of this nature are possible any time that two mutually
   distrustful entities control requests or responses that are placed
   onto a single HTTP/3 connection.  If the shared QPACK compressor
   permits one entity to add entries to the dynamic table, and the other
   to refer to those entries while encoding chosen field lines, then the
   attacker (the second entity) can learn the state of the table by
   observing the length of the encoded output.

   For example, requests or responses from mutually distrustful entities
   can occur when an intermediary either:

   *  sends requests from multiple clients on a single connection toward
      an origin server, or

   *  takes responses from multiple origin servers and places them on a
      shared connection toward a client.

   Web browsers also need to assume that requests made on the same
   connection by different web origins ([RFC6454]) are made by mutually
   distrustful entities.  Other scenarios involving mutually distrustful
   entities are also possible.

### 7.1.2  Mitigation

   Users of HTTP that require confidentiality for header or trailer
   fields can use values with entropy sufficient to make guessing
   infeasible.  However, this is impractical as a general solution
   because it forces all users of HTTP to take steps to mitigate
   attacks.  It would impose new constraints on how HTTP is used.

   Rather than impose constraints on users of HTTP, an implementation of
   QPACK can instead constrain how compression is applied in order to
   limit the potential for dynamic table probing.

   An ideal solution segregates access to the dynamic table based on the
   entity that is constructing the message.  Field values that are added
   to the table are attributed to an entity, and only the entity that
   created a particular value can extract that value.

   To improve compression performance of this option, certain entries
   might be tagged as being public.  For example, a web browser might
   make the values of the Accept-Encoding header field available in all
   requests.

   An encoder without good knowledge of the provenance of field values
   might instead introduce a penalty for many field lines with the same
   field name and different values.  This penalty could cause a large
   number of attempts to guess a field value to result in the field not
   being compared to the dynamic table entries in future messages,
   effectively preventing further guesses.

   This response might be made inversely proportional to the length of
   the field value.  Disabling access to the dynamic table for a given
   field name might occur for shorter values more quickly or with higher
   probability than for longer values.

   This mitigation is most effective between two endpoints.  If messages
   are re-encoded by an intermediary without knowledge of which entity
   constructed a given message, the intermediary could inadvertently
   merge compression contexts that the original encoder had specifically
   kept separate.

      |  Note: Simply removing entries corresponding to the field from
      |  the dynamic table can be ineffectual if the attacker has a
      |  reliable way of causing values to be reinstalled.  For example,
      |  a request to load an image in a web browser typically includes
      |  the Cookie header field (a potentially highly valued target for
      |  this sort of attack), and websites can easily force an image to
      |  be loaded, thereby refreshing the entry in the dynamic table.

### 7.1.3  Never-Indexed Literals

   Implementations can also choose to protect sensitive fields by not
   compressing them and instead encoding their value as literals.

   Refusing to insert a field line into the dynamic table is only
   effective if doing so is avoided on all hops.  The never-indexed
   literal bit (see Section 4.5.4) can be used to signal to
   intermediaries that a particular value was intentionally sent as a
   literal.

> **MUST NOT**: An intermediary MUST NOT re-encode a value that uses a literal
   representation with the 'N' bit set with another representation that
   would index it.  If QPACK is used for re-encoding, a literal
> **MUST**: representation with the 'N' bit set MUST be used.  If HPACK is used
   for re-encoding, the never-indexed literal representation (see
> **MUST**: Section 6.2.3 of [RFC7541]) MUST be used.

   The choice to mark that a field value should never be indexed depends
   on several factors.  Since QPACK does not protect against guessing an
   entire field value, short or low-entropy values are more readily
   recovered by an adversary.  Therefore, an encoder might choose not to
   index values with low entropy.

   An encoder might also choose not to index values for fields that are
   considered to be highly valuable or sensitive to recovery, such as
   the Cookie or Authorization header fields.

   On the contrary, an encoder might prefer indexing values for fields
   that have little or no value if they were exposed.  For instance, a
   User-Agent header field does not commonly vary between requests and
   is sent to any server.  In that case, confirmation that a particular
   User-Agent value has been used provides little value.

   Note that these criteria for deciding to use a never-indexed literal
   representation will evolve over time as new attacks are discovered.

## 7.2  Static Huffman Encoding

   There is no currently known attack against a static Huffman encoding.
   A study has shown that using a static Huffman encoding table created
   an information leakage; however, this same study concluded that an
   attacker could not take advantage of this information leakage to
   recover any meaningful amount of information (see [PETAL]).

## 7.3  Memory Consumption

   An attacker can try to cause an endpoint to exhaust its memory.
   QPACK is designed to limit both the peak and stable amounts of memory
   allocated by an endpoint.

   QPACK uses the definition of the maximum size of the dynamic table
   and the maximum number of blocking streams to limit the amount of
   memory the encoder can cause the decoder to consume.  In HTTP/3,
   these values are controlled by the decoder through the settings
   parameters SETTINGS_QPACK_MAX_TABLE_CAPACITY and
   SETTINGS_QPACK_BLOCKED_STREAMS, respectively (see Section 3.2.3 and
   Section 2.1.2).  The limit on the size of the dynamic table takes
   into account the size of the data stored in the dynamic table, plus a
   small allowance for overhead.  The limit on the number of blocked
   streams is only a proxy for the maximum amount of memory required by
   the decoder.  The actual maximum amount of memory will depend on how
   much memory the decoder uses to track each blocked stream.

   A decoder can limit the amount of state memory used for the dynamic
   table by setting an appropriate value for the maximum size of the
   dynamic table.  In HTTP/3, this is realized by setting an appropriate
   value for the SETTINGS_QPACK_MAX_TABLE_CAPACITY parameter.  An
   encoder can limit the amount of state memory it uses by choosing a
   smaller dynamic table size than the decoder allows and signaling this
   to the decoder (see Section 4.3.1).

   A decoder can limit the amount of state memory used for blocked
   streams by setting an appropriate value for the maximum number of
   blocked streams.  In HTTP/3, this is realized by setting an
   appropriate value for the SETTINGS_QPACK_BLOCKED_STREAMS parameter.
   Streams that risk becoming blocked consume no additional state memory
   on the encoder.

   An encoder allocates memory to track all dynamic table references in
   unacknowledged field sections.  An implementation can directly limit
   the amount of state memory by only using as many references to the
   dynamic table as it wishes to track; no signaling to the decoder is
   required.  However, limiting references to the dynamic table will
   reduce compression effectiveness.

   The amount of temporary memory consumed by an encoder or decoder can
   be limited by processing field lines sequentially.  A decoder
   implementation does not need to retain a complete list of field lines
   while decoding a field section.  An encoder implementation does not
   need to retain a complete list of field lines while encoding a field
   section if it is using a single-pass algorithm.  Note that it might
   be necessary for an application to retain a complete list of field
   lines for other reasons; even if QPACK does not force this to occur,
   application constraints might make this necessary.

   While the negotiated limit on the dynamic table size accounts for
   much of the memory that can be consumed by a QPACK implementation,
   data that cannot be immediately sent due to flow control is not
   affected by this limit.  Implementations should limit the size of
   unsent data, especially on the decoder stream where flexibility to
   choose what to send is limited.  Possible responses to an excess of
   unsent data might include limiting the ability of the peer to open
   new streams, reading only from the encoder stream, or closing the
   connection.

## 7.4  Implementation Limits

   An implementation of QPACK needs to ensure that large values for
   integers, long encoding for integers, or long string literals do not
   create security weaknesses.

   An implementation has to set a limit for the values it accepts for
   integers, as well as for the encoded length; see Section 4.1.1.  In
   the same way, it has to set a limit to the length it accepts for
> **SHOULD**: string literals; see Section 4.1.2.  These limits SHOULD be large
   enough to process the largest individual field the HTTP
   implementation can be configured to accept.

   If an implementation encounters a value larger than it is able to
> **MUST**: decode, this MUST be treated as a stream error of type
   QPACK_DECOMPRESSION_FAILED if on a request stream or a connection
   error of the appropriate type if on the encoder or decoder stream.

---

**Navigation:** [[../RFC9204|RFC9204 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
