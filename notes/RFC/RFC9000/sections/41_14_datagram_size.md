---
title: "14.  Datagram Size"
rfc_number: 9000
rfc_section: "14"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 14: Datagram Size — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, datagram_size]
---

# 14.  Datagram Size


   A UDP datagram can include one or more QUIC packets.  The datagram
   size refers to the total UDP payload size of a single UDP datagram
   carrying QUIC packets.  The datagram size includes one or more QUIC
   packet headers and protected payloads, but not the UDP or IP headers.

   The maximum datagram size is defined as the largest size of UDP
   payload that can be sent across a network path using a single UDP
> **MUST NOT**: datagram.  QUIC MUST NOT be used if the network path cannot support a
   maximum datagram size of at least 1200 bytes.

   QUIC assumes a minimum IP packet size of at least 1280 bytes.  This
   is the IPv6 minimum size [IPv6] and is also supported by most modern
   IPv4 networks.  Assuming the minimum IP header size of 40 bytes for
   IPv6 and 20 bytes for IPv4 and a UDP header size of 8 bytes, this
   results in a maximum datagram size of 1232 bytes for IPv6 and 1252
   bytes for IPv4.  Thus, modern IPv4 and all IPv6 network paths are
   expected to be able to support QUIC.

      |  Note: This requirement to support a UDP payload of 1200 bytes
      |  limits the space available for IPv6 extension headers to 32
      |  bytes or IPv4 options to 52 bytes if the path only supports the
      |  IPv6 minimum MTU of 1280 bytes.  This affects Initial packets
      |  and path validation.

   Any maximum datagram size larger than 1200 bytes can be discovered
   using Path Maximum Transmission Unit Discovery (PMTUD) (see
   Section 14.2.1) or Datagram Packetization Layer PMTU Discovery
   (DPLPMTUD) (see Section 14.3).

   Enforcement of the max_udp_payload_size transport parameter
   (Section 18.2) might act as an additional limit on the maximum
   datagram size.  A sender can avoid exceeding this limit, once the
   value is known.  However, prior to learning the value of the
   transport parameter, endpoints risk datagrams being lost if they send
   datagrams larger than the smallest allowed maximum datagram size of
   1200 bytes.

> **MUST NOT**: UDP datagrams MUST NOT be fragmented at the IP layer.  In IPv4
   [IPv4], the Don't Fragment (DF) bit MUST be set if possible, to
   prevent fragmentation on the path.

   QUIC sometimes requires datagrams to be no smaller than a certain
   size; see Section 8.1 as an example.  However, the size of a datagram
   is not authenticated.  That is, if an endpoint receives a datagram of
   a certain size, it cannot know that the sender sent the datagram at
> **MUST NOT**: the same size.  Therefore, an endpoint MUST NOT close a connection
   when it receives a datagram that does not meet size constraints; the
> **MAY**: endpoint MAY discard such datagrams.

## 14.1.  Initial Datagram Size

> **MUST**: A client MUST expand the payload of all UDP datagrams carrying
   Initial packets to at least the smallest allowed maximum datagram
   size of 1200 bytes by adding PADDING frames to the Initial packet or
   by coalescing the Initial packet; see Section 12.2.  Initial packets
   can even be coalesced with invalid packets, which a receiver will
> **MUST**: discard.  Similarly, a server MUST expand the payload of all UDP
   datagrams carrying ack-eliciting Initial packets to at least the
   smallest allowed maximum datagram size of 1200 bytes.

   Sending UDP datagrams of this size ensures that the network path
   supports a reasonable Path Maximum Transmission Unit (PMTU), in both
   directions.  Additionally, a client that expands Initial packets
   helps reduce the amplitude of amplification attacks caused by server
   responses toward an unverified client address; see Section 8.

> **MAY**: Datagrams containing Initial packets MAY exceed 1200 bytes if the
   sender believes that the network path and peer both support the size
   that it chooses.

> **MUST**: A server MUST discard an Initial packet that is carried in a UDP
   datagram with a payload that is smaller than the smallest allowed
> **MAY**: maximum datagram size of 1200 bytes.  A server MAY also immediately
   close the connection by sending a CONNECTION_CLOSE frame with an
   error code of PROTOCOL_VIOLATION; see Section 10.2.3.

> **MUST**: The server MUST also limit the number of bytes it sends before
   validating the address of the client; see Section 8.

## 14.2.  Path Maximum Transmission Unit

   The PMTU is the maximum size of the entire IP packet, including the
   IP header, UDP header, and UDP payload.  The UDP payload includes one
   or more QUIC packet headers and protected payloads.  The PMTU can
   depend on path characteristics and can therefore change over time.
   The largest UDP payload an endpoint sends at any given time is
   referred to as the endpoint's maximum datagram size.

> **SHOULD**: An endpoint SHOULD use DPLPMTUD (Section 14.3) or PMTUD
   (Section 14.2.1) to determine whether the path to a destination will
   support a desired maximum datagram size without fragmentation.  In
> **SHOULD NOT**: the absence of these mechanisms, QUIC endpoints SHOULD NOT send
   datagrams larger than the smallest allowed maximum datagram size.

   Both DPLPMTUD and PMTUD send datagrams that are larger than the
   current maximum datagram size, referred to as PMTU probes.  All QUIC
> **SHOULD**: packets that are not sent in a PMTU probe SHOULD be sized to fit
   within the maximum datagram size to avoid the datagram being
   fragmented or dropped [RFC8085].

   If a QUIC endpoint determines that the PMTU between any pair of local
   and remote IP addresses cannot support the smallest allowed maximum
> **MUST**: datagram size of 1200 bytes, it MUST immediately cease sending QUIC
   packets, except for those in PMTU probes or those containing
> **MAY**: CONNECTION_CLOSE frames, on the affected path.  An endpoint MAY
   terminate the connection if an alternative path cannot be found.

   Each pair of local and remote addresses could have a different PMTU.
   QUIC implementations that implement any kind of PMTU discovery
> **SHOULD**: therefore SHOULD maintain a maximum datagram size for each
   combination of local and remote IP addresses.

> **MAY**: A QUIC implementation MAY be more conservative in computing the
   maximum datagram size to allow for unknown tunnel overheads or IP
   header options/extensions.

### 14.2.1.  Handling of ICMP Messages by PMTUD

   PMTUD [RFC1191] [RFC8201] relies on reception of ICMP messages (that
   is, IPv6 Packet Too Big (PTB) messages) that indicate when an IP
   packet is dropped because it is larger than the local router MTU.
   DPLPMTUD can also optionally use these messages.  This use of ICMP
   messages is potentially vulnerable to attacks by entities that cannot
   observe packets but might successfully guess the addresses used on
   the path.  These attacks could reduce the PMTU to a bandwidth-
   inefficient value.

> **MUST**: An endpoint MUST ignore an ICMP message that claims the PMTU has
   decreased below QUIC's smallest allowed maximum datagram size.

   The requirements for generating ICMP [RFC1812] [RFC4443] state that
   the quoted packet should contain as much of the original packet as
   possible without exceeding the minimum MTU for the IP version.  The
   size of the quoted packet can actually be smaller, or the information
   unintelligible, as described in Section 1.1 of [DPLPMTUD].

> **SHOULD**: QUIC endpoints using PMTUD SHOULD validate ICMP messages to protect
   from packet injection as specified in [RFC8201] and Section 5.2 of
> **SHOULD**: [RFC8085].  This validation SHOULD use the quoted packet supplied in
   the payload of an ICMP message to associate the message with a
   corresponding transport connection (see Section 4.6.1 of [DPLPMTUD]).
> **MUST**: ICMP message validation MUST include matching IP addresses and UDP
   ports [RFC8085] and, when possible, connection IDs to an active QUIC
> **SHOULD**: session.  The endpoint SHOULD ignore all ICMP messages that fail
   validation.

> **MUST NOT**: An endpoint MUST NOT increase the PMTU based on ICMP messages; see
   Item 6 in Section 3 of [DPLPMTUD].  Any reduction in QUIC's maximum
> **MAY**: datagram size in response to ICMP messages MAY be provisional until
   QUIC's loss detection algorithm determines that the quoted packet has
   actually been lost.

## 14.3.  Datagram Packetization Layer PMTU Discovery

   DPLPMTUD [DPLPMTUD] relies on tracking loss or acknowledgment of QUIC
   packets that are carried in PMTU probes.  PMTU probes for DPLPMTUD
   that use the PADDING frame implement "Probing using padding data", as
   defined in Section 4.1 of [DPLPMTUD].

> **SHOULD**: Endpoints SHOULD set the initial value of BASE_PLPMTU (Section 5.1 of
   [DPLPMTUD]) to be consistent with QUIC's smallest allowed maximum
   datagram size.  The MIN_PLPMTU is the same as the BASE_PLPMTU.

   QUIC endpoints implementing DPLPMTUD maintain a DPLPMTUD Maximum
   Packet Size (MPS) (Section 4.4 of [DPLPMTUD]) for each combination of
   local and remote IP addresses.  This corresponds to the maximum
   datagram size.

### 14.3.1.  DPLPMTUD and Initial Connectivity

   From the perspective of DPLPMTUD, QUIC is an acknowledged
   Packetization Layer (PL).  A QUIC sender can therefore enter the
   DPLPMTUD BASE state (Section 5.2 of [DPLPMTUD]) when the QUIC
   connection handshake has been completed.

### 14.3.2.  Validating the Network Path with DPLPMTUD

   QUIC is an acknowledged PL; therefore, a QUIC sender does not
   implement a DPLPMTUD CONFIRMATION_TIMER while in the SEARCH_COMPLETE
   state; see Section 5.2 of [DPLPMTUD].

### 14.3.3.  Handling of ICMP Messages by DPLPMTUD

   An endpoint using DPLPMTUD requires the validation of any received
   ICMP PTB message before using the PTB information, as defined in
   Section 4.6 of [DPLPMTUD].  In addition to UDP port validation, QUIC
   validates an ICMP message by using other PL information (e.g.,
   validation of connection IDs in the quoted packet of any received
   ICMP message).

   The considerations for processing ICMP messages described in
   Section 14.2.1 also apply if these messages are used by DPLPMTUD.

## 14.4.  Sending QUIC PMTU Probes

   PMTU probes are ack-eliciting packets.

   Endpoints could limit the content of PMTU probes to PING and PADDING
   frames, since packets that are larger than the current maximum
   datagram size are more likely to be dropped by the network.  Loss of
   a QUIC packet that is carried in a PMTU probe is therefore not a
> **SHOULD NOT**: reliable indication of congestion and SHOULD NOT trigger a congestion
   control reaction; see Item 7 in Section 3 of [DPLPMTUD].  However,
   PMTU probes consume congestion window, which could delay subsequent
   transmission by an application.

### 14.4.1.  PMTU Probes Containing Source Connection ID

   Endpoints that rely on the Destination Connection ID field for
   routing incoming QUIC packets are likely to require that the
   connection ID be included in PMTU probes to route any resulting ICMP
   messages (Section 14.2.1) back to the correct endpoint.  However,
   only long header packets (Section 17.2) contain the Source Connection
   ID field, and long header packets are not decrypted or acknowledged
   by the peer once the handshake is complete.

   One way to construct a PMTU probe is to coalesce (see Section 12.2) a
   packet with a long header, such as a Handshake or 0-RTT packet
   (Section 17.2), with a short header packet in a single UDP datagram.
   If the resulting PMTU probe reaches the endpoint, the packet with the
   long header will be ignored, but the short header packet will be
   acknowledged.  If the PMTU probe causes an ICMP message to be sent,
   the first part of the probe will be quoted in that message.  If the
   Source Connection ID field is within the quoted portion of the probe,
   that could be used for routing or validation of the ICMP message.

      |  Note: The purpose of using a packet with a long header is only
      |  to ensure that the quoted packet contained in the ICMP message
      |  contains a Source Connection ID field.  This packet does not
      |  need to be a valid packet, and it can be sent even if there is
      |  no current use for packets of that type.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
