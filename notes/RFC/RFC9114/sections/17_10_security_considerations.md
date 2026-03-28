---
title: "10.  Security Considerations"
rfc_number: 9114
rfc_section: "10"
source_url: "https://www.rfc-editor.org/rfc/rfc9114"
description: "Section 10: Security Considerations — RFC 9114 — HTTP/3"
tags: [RFC9114, HTTP/3, QUIC, variable-length-frames, unidirectional-streams, QPACK, SETTINGS, GOAWAY, server-push, security_considerations]
---

## 10.  Security Considerations

10.  Security Considerations

   The security considerations of HTTP/3 should be comparable to those
   of HTTP/2 with TLS.  However, many of the considerations from
   Section 10 of [HTTP/2] apply to [QUIC-TRANSPORT] and are discussed in
   that document.

## 10.1  Server Authority

   HTTP/3 relies on the HTTP definition of authority.  The security
   considerations of establishing authority are discussed in
   Section 17.1 of [HTTP].

## 10.2  Cross-Protocol Attacks

   The use of ALPN in the TLS and QUIC handshakes establishes the target
   application protocol before application-layer bytes are processed.
   This ensures that endpoints have strong assurances that peers are
   using the same protocol.

   This does not guarantee protection from all cross-protocol attacks.
   Section 21.5 of [QUIC-TRANSPORT] describes some ways in which the
   plaintext of QUIC packets can be used to perform request forgery
   against endpoints that don't use authenticated transports.

## 10.3  Intermediary-Encapsulation Attacks

   The HTTP/3 field encoding allows the expression of names that are not
   valid field names in the syntax used by HTTP (Section 5.1 of [HTTP]).
> **MUST**: Requests or responses containing invalid field names MUST be treated
   as malformed.  Therefore, an intermediary cannot translate an HTTP/3
   request or response containing an invalid field name into an HTTP/1.1
   message.

   Similarly, HTTP/3 can transport field values that are not valid.
   While most values that can be encoded will not alter field parsing,
   carriage return (ASCII 0x0d), line feed (ASCII 0x0a), and the null
   character (ASCII 0x00) might be exploited by an attacker if they are
   translated verbatim.  Any request or response that contains a
> **MUST**: character not permitted in a field value MUST be treated as
   malformed.  Valid characters are defined by the "field-content" ABNF
   rule in Section 5.5 of [HTTP].

## 10.4  Cacheability of Pushed Responses

   Pushed responses do not have an explicit request from the client; the
   request is provided by the server in the PUSH_PROMISE frame.

   Caching responses that are pushed is possible based on the guidance
   provided by the origin server in the Cache-Control header field.
   However, this can cause issues if a single server hosts more than one
   tenant.  For example, a server might offer multiple users each a
   small portion of its URI space.

   Where multiple tenants share space on the same server, that server
> **MUST**: MUST ensure that tenants are not able to push representations of
   resources that they do not have authority over.  Failure to enforce
   this would allow a tenant to provide a representation that would be
   served out of cache, overriding the actual representation that the
   authoritative tenant provides.

   Clients are required to reject pushed responses for which an origin
   server is not authoritative; see Section 4.6.

## 10.5  Denial-of-Service Considerations

   An HTTP/3 connection can demand a greater commitment of resources to
   operate than an HTTP/1.1 or HTTP/2 connection.  The use of field
   compression and flow control depend on a commitment of resources for
   storing a greater amount of state.  Settings for these features
   ensure that memory commitments for these features are strictly
   bounded.

   The number of PUSH_PROMISE frames is constrained in a similar
> **SHOULD**: fashion.  A client that accepts server push SHOULD limit the number
   of push IDs it issues at a time.

   Processing capacity cannot be guarded as effectively as state
   capacity.

   The ability to send undefined protocol elements that the peer is
   required to ignore can be abused to cause a peer to expend additional
   processing time.  This might be done by setting multiple undefined
   SETTINGS parameters, unknown frame types, or unknown stream types.
   Note, however, that some uses are entirely legitimate, such as
   optional-to-understand extensions and padding to increase resistance
   to traffic analysis.

   Compression of field sections also offers some opportunities to waste
   processing resources; see Section 7 of [QPACK] for more details on
   potential abuses.

   All these features -- i.e., server push, unknown protocol elements,
   field compression -- have legitimate uses.  These features become a
   burden only when they are used unnecessarily or to excess.

   An endpoint that does not monitor such behavior exposes itself to a
> **SHOULD**: risk of denial-of-service attack.  Implementations SHOULD track the
   use of these features and set limits on their use.  An endpoint MAY
   treat activity that is suspicious as a connection error of type
   H3_EXCESSIVE_LOAD, but false positives will result in disrupting
   valid connections and requests.

### 10.5.1  Limits on Field Section Size

   A large field section (Section 4.1) can cause an implementation to
   commit a large amount of state.  Header fields that are critical for
   routing can appear toward the end of a header section, which prevents
   streaming of the header section to its ultimate destination.  This
   ordering and other reasons, such as ensuring cache correctness, mean
   that an endpoint likely needs to buffer the entire header section.
   Since there is no hard limit to the size of a field section, some
   endpoints could be forced to commit a large amount of available
   memory for header fields.

   An endpoint can use the SETTINGS_MAX_FIELD_SECTION_SIZE
   (Section 4.2.2) setting to advise peers of limits that might apply on
   the size of field sections.  This setting is only advisory, so
> **MAY**: endpoints MAY choose to send field sections that exceed this limit
   and risk having the request or response being treated as malformed.
   This setting is specific to an HTTP/3 connection, so any request or
   response could encounter a hop with a lower, unknown limit.  An
   intermediary can attempt to avoid this problem by passing on values
   presented by different peers, but they are not obligated to do so.

   A server that receives a larger field section than it is willing to
   handle can send an HTTP 431 (Request Header Fields Too Large) status
   code ([RFC6585]).  A client can discard responses that it cannot
   process.

### 10.5.2  CONNECT Issues

   The CONNECT method can be used to create disproportionate load on a
   proxy, since stream creation is relatively inexpensive when compared
   to the creation and maintenance of a TCP connection.  Therefore, a
   proxy that supports CONNECT might be more conservative in the number
   of simultaneous requests it accepts.

   A proxy might also maintain some resources for a TCP connection
   beyond the closing of the stream that carries the CONNECT request,
   since the outgoing TCP connection remains in the TIME_WAIT state.  To
   account for this, a proxy might delay increasing the QUIC stream
   limits for some time after a TCP connection terminates.

## 10.6  Use of Compression

   Compression can allow an attacker to recover secret data when it is
   compressed in the same context as data under attacker control.
   HTTP/3 enables compression of fields (Section 4.2); the following
   concerns also apply to the use of HTTP compressed content-codings;
   see Section 8.4.1 of [HTTP].

   There are demonstrable attacks on compression that exploit the
   characteristics of the web (e.g., [BREACH]).  The attacker induces
   multiple requests containing varying plaintext, observing the length
   of the resulting ciphertext in each, which reveals a shorter length
   when a guess about the secret is correct.

> **MUST NOT**: Implementations communicating on a secure channel MUST NOT compress
   content that includes both confidential and attacker-controlled data
   unless separate compression contexts are used for each source of
> **MUST NOT**: data.  Compression MUST NOT be used if the source of data cannot be
   reliably determined.

   Further considerations regarding the compression of field sections
   are described in [QPACK].

## 10.7  Padding and Traffic Analysis

   Padding can be used to obscure the exact size of frame content and is
   provided to mitigate specific attacks within HTTP, for example,
   attacks where compressed content includes both attacker-controlled
   plaintext and secret data (e.g., [BREACH]).

   Where HTTP/2 employs PADDING frames and Padding fields in other
   frames to make a connection more resistant to traffic analysis,
   HTTP/3 can either rely on transport-layer padding or employ the
   reserved frame and stream types discussed in Sections 7.2.8 and
### 6.2.3  These methods of padding produce different results in terms
   of the granularity of padding, how padding is arranged in relation to
   the information that is being protected, whether padding is applied
   in the case of packet loss, and how an implementation might control
   padding.

   Reserved stream types can be used to give the appearance of sending
   traffic even when the connection is idle.  Because HTTP traffic often
   occurs in bursts, apparent traffic can be used to obscure the timing
   or duration of such bursts, even to the point of appearing to send a
   constant stream of data.  However, as such traffic is still flow
   controlled by the receiver, a failure to promptly drain such streams
   and provide additional flow-control credit can limit the sender's
   ability to send real traffic.

   To mitigate attacks that rely on compression, disabling or limiting
   compression might be preferable to padding as a countermeasure.

   Use of padding can result in less protection than might seem
   immediately obvious.  Redundant padding could even be
   counterproductive.  At best, padding only makes it more difficult for
   an attacker to infer length information by increasing the number of
   frames an attacker has to observe.  Incorrectly implemented padding
   schemes can be easily defeated.  In particular, randomized padding
   with a predictable distribution provides very little protection;
   similarly, padding payloads to a fixed size exposes information as
   payload sizes cross the fixed-sized boundary, which could be possible
   if an attacker can control plaintext.

## 10.8  Frame Parsing

   Several protocol elements contain nested length elements, typically
   in the form of frames with an explicit length containing variable-
   length integers.  This could pose a security risk to an incautious
> **MUST**: implementer.  An implementation MUST ensure that the length of a
   frame exactly matches the length of the fields it contains.

## 10.9  Early Data

   The use of 0-RTT with HTTP/3 creates an exposure to replay attack.
> **MUST**: The anti-replay mitigations in [HTTP-REPLAY] MUST be applied when
   using HTTP/3 with 0-RTT.  When applying [HTTP-REPLAY] to HTTP/3,
   references to the TLS layer refer to the handshake performed within
   QUIC, while all references to application data refer to the contents
   of streams.

## 10.10  Migration

   Certain HTTP implementations use the client address for logging or
   access-control purposes.  Since a QUIC client's address might change
   during a connection (and future versions might support simultaneous
   use of multiple addresses), such implementations will need to either
   actively retrieve the client's current address or addresses when they
   are relevant or explicitly accept that the original address might
   change.

## 10.11  Privacy Considerations

   Several characteristics of HTTP/3 provide an observer an opportunity
   to correlate actions of a single client or server over time.  These
   include the value of settings, the timing of reactions to stimulus,
   and the handling of any features that are controlled by settings.

   As far as these create observable differences in behavior, they could
   be used as a basis for fingerprinting a specific client.

   HTTP/3's preference for using a single QUIC connection allows
   correlation of a user's activity on a site.  Reusing connections for
   different origins allows for correlation of activity across those
   origins.

   Several features of QUIC solicit immediate responses and can be used
   by an endpoint to measure latency to their peer; this might have
   privacy implications in certain scenarios.

---

**Navigation:** [[../RFC9114|RFC9114 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
