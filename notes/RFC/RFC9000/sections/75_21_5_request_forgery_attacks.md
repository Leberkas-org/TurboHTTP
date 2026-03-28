---
title: "21.5.  Request Forgery Attacks"
rfc_number: 9000
rfc_section: "21.5"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 21.5: Request Forgery Attacks — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, request_forgery_attacks]
---

# 21.5.  Request Forgery Attacks


   A request forgery attack occurs where an endpoint causes its peer to
   issue a request towards a victim, with the request controlled by the
   endpoint.  Request forgery attacks aim to provide an attacker with
   access to capabilities of its peer that might otherwise be
   unavailable to the attacker.  For a networking protocol, a request
   forgery attack is often used to exploit any implicit authorization
   conferred on the peer by the victim due to the peer's location in the
   network.

   For request forgery to be effective, an attacker needs to be able to
   influence what packets the peer sends and where these packets are
   sent.  If an attacker can target a vulnerable service with a
   controlled payload, that service might perform actions that are
   attributed to the attacker's peer but are decided by the attacker.

   For example, cross-site request forgery [CSRF] exploits on the Web
   cause a client to issue requests that include authorization cookies
   [COOKIE], allowing one site access to information and actions that
   are intended to be restricted to a different site.

   As QUIC runs over UDP, the primary attack modality of concern is one
   where an attacker can select the address to which its peer sends UDP
   datagrams and can control some of the unprotected content of those
   packets.  As much of the data sent by QUIC endpoints is protected,
   this includes control over ciphertext.  An attack is successful if an
   attacker can cause a peer to send a UDP datagram to a host that will
   perform some action based on content in the datagram.

   This section discusses ways in which QUIC might be used for request
   forgery attacks.

   This section also describes limited countermeasures that can be
   implemented by QUIC endpoints.  These mitigations can be employed
   unilaterally by a QUIC implementation or deployment, without
   potential targets for request forgery attacks taking action.
   However, these countermeasures could be insufficient if UDP-based
   services do not properly authorize requests.

   Because the migration attack described in Section 21.5.4 is quite
   powerful and does not have adequate countermeasures, QUIC server
   implementations should assume that attackers can cause them to
   generate arbitrary UDP payloads to arbitrary destinations.  QUIC
> **SHOULD NOT**: servers SHOULD NOT be deployed in networks that do not deploy ingress
   filtering [BCP38] and also have inadequately secured UDP endpoints.

   Although it is not generally possible to ensure that clients are not
   co-located with vulnerable endpoints, this version of QUIC does not
   allow servers to migrate, thus preventing spoofed migration attacks
> **MUST**: on clients.  Any future extension that allows server migration MUST
   also define countermeasures for forgery attacks.

### 21.5.1.  Control Options for Endpoints

   QUIC offers some opportunities for an attacker to influence or
   control where its peer sends UDP datagrams:

   *  initial connection establishment (Section 7), where a server is
      able to choose where a client sends datagrams -- for example, by
      populating DNS records;

   *  preferred addresses (Section 9.6), where a server is able to
      choose where a client sends datagrams;

   *  spoofed connection migrations (Section 9.3.1), where a client is
      able to use source address spoofing to select where a server sends
      subsequent datagrams; and

   *  spoofed packets that cause a server to send a Version Negotiation
      packet (Section 21.5.5).

   In all cases, the attacker can cause its peer to send datagrams to a
   victim that might not understand QUIC.  That is, these packets are
   sent by the peer prior to address validation; see Section 8.

   Outside of the encrypted portion of packets, QUIC offers an endpoint
   several options for controlling the content of UDP datagrams that its
   peer sends.  The Destination Connection ID field offers direct
   control over bytes that appear early in packets sent by the peer; see
   Section 5.1.  The Token field in Initial packets offers a server
   control over other bytes of Initial packets; see Section 17.2.2.

   There are no measures in this version of QUIC to prevent indirect
   control over the encrypted portions of packets.  It is necessary to
   assume that endpoints are able to control the contents of frames that
   a peer sends, especially those frames that convey application data,
   such as STREAM frames.  Though this depends to some degree on details
   of the application protocol, some control is possible in many
   protocol usage contexts.  As the attacker has access to packet
   protection keys, they are likely to be capable of predicting how a
   peer will encrypt future packets.  Successful control over datagram
   content then only requires that the attacker be able to predict the
   packet number and placement of frames in packets with some amount of
   reliability.

   This section assumes that limiting control over datagram content is
   not feasible.  The focus of the mitigations in subsequent sections is
   on limiting the ways in which datagrams that are sent prior to
   address validation can be used for request forgery.

### 21.5.2.  Request Forgery with Client Initial Packets

   An attacker acting as a server can choose the IP address and port on
   which it advertises its availability, so Initial packets from clients
   are assumed to be available for use in this sort of attack.  The
   address validation implicit in the handshake ensures that -- for a
   new connection -- a client will not send other types of packets to a
   destination that does not understand QUIC or is not willing to accept
   a QUIC connection.

   Initial packet protection (Section 5.2 of [QUIC-TLS]) makes it
   difficult for servers to control the content of Initial packets sent
   by clients.  A client choosing an unpredictable Destination
   Connection ID ensures that servers are unable to control any of the
   encrypted portion of Initial packets from clients.

   However, the Token field is open to server control and does allow a
   server to use clients to mount request forgery attacks.  The use of
   tokens provided with the NEW_TOKEN frame (Section 8.1.3) offers the
   only option for request forgery during connection establishment.

   Clients, however, are not obligated to use the NEW_TOKEN frame.
   Request forgery attacks that rely on the Token field can be avoided
   if clients send an empty Token field when the server address has
   changed from when the NEW_TOKEN frame was received.

   Clients could avoid using NEW_TOKEN if the server address changes.
   However, not including a Token field could adversely affect
   performance.  Servers could rely on NEW_TOKEN to enable the sending
   of data in excess of the three-times limit on sending data; see
   Section 8.1.  In particular, this affects cases where clients use
   0-RTT to request data from servers.

   Sending a Retry packet (Section 17.2.5) offers a server the option to
   change the Token field.  After sending a Retry, the server can also
   control the Destination Connection ID field of subsequent Initial
   packets from the client.  This also might allow indirect control over
   the encrypted content of Initial packets.  However, the exchange of a
   Retry packet validates the server's address, thereby preventing the
   use of subsequent Initial packets for request forgery.

### 21.5.3.  Request Forgery with Preferred Addresses

   Servers can specify a preferred address, which clients then migrate
   to after confirming the handshake; see Section 9.6.  The Destination
   Connection ID field of packets that the client sends to a preferred
   address can be used for request forgery.

> **MUST NOT**: A client MUST NOT send non-probing frames to a preferred address
   prior to validating that address; see Section 8.  This greatly
   reduces the options that a server has to control the encrypted
   portion of datagrams.

   This document does not offer any additional countermeasures that are
   specific to the use of preferred addresses and can be implemented by
   endpoints.  The generic measures described in Section 21.5.6 could be
   used as further mitigation.

### 21.5.4.  Request Forgery with Spoofed Migration

   Clients are able to present a spoofed source address as part of an
   apparent connection migration to cause a server to send datagrams to
   that address.

   The Destination Connection ID field in any packets that a server
   subsequently sends to this spoofed address can be used for request
   forgery.  A client might also be able to influence the ciphertext.

   A server that only sends probing packets (Section 9.1) to an address
   prior to address validation provides an attacker with only limited
   control over the encrypted portion of datagrams.  However,
   particularly for NAT rebinding, this can adversely affect
   performance.  If the server sends frames carrying application data,
   an attacker might be able to control most of the content of
   datagrams.

   This document does not offer specific countermeasures that can be
   implemented by endpoints, aside from the generic measures described
   in Section 21.5.6.  However, countermeasures for address spoofing at
   the network level -- in particular, ingress filtering [BCP38] -- are
   especially effective against attacks that use spoofing and originate
   from an external network.

### 21.5.5.  Request Forgery with Version Negotiation

   Clients that are able to present a spoofed source address on a packet
   can cause a server to send a Version Negotiation packet
   (Section 17.2.1) to that address.

   The absence of size restrictions on the connection ID fields for
   packets of an unknown version increases the amount of data that the
   client controls from the resulting datagram.  The first byte of this
   packet is not under client control and the next four bytes are zero,
   but the client is able to control up to 512 bytes starting from the
   fifth byte.

   No specific countermeasures are provided for this attack, though
   generic protections (Section 21.5.6) could apply.  In this case,
   ingress filtering [BCP38] is also effective.

### 21.5.6.  Generic Request Forgery Countermeasures

   The most effective defense against request forgery attacks is to
   modify vulnerable services to use strong authentication.  However,
   this is not always something that is within the control of a QUIC
   deployment.  This section outlines some other steps that QUIC
   endpoints could take unilaterally.  These additional steps are all
   discretionary because, depending on circumstances, they could
   interfere with or prevent legitimate uses.

   Services offered over loopback interfaces often lack proper
> **MAY**: authentication.  Endpoints MAY prevent connection attempts or
   migration to a loopback address.  Endpoints SHOULD NOT allow
   connections or migration to a loopback address if the same service
   was previously available at a different interface or if the address
   was provided by a service at a non-loopback address.  Endpoints that
   depend on these capabilities could offer an option to disable these
   protections.

   Similarly, endpoints could regard a change in address to a link-local
   address [RFC4291] or an address in a private-use range [RFC1918] from
   a global, unique-local [RFC4193], or non-private address as a
   potential attempt at request forgery.  Endpoints could refuse to use
   these addresses entirely, but that carries a significant risk of
> **SHOULD NOT**: interfering with legitimate uses.  Endpoints SHOULD NOT refuse to use
   an address unless they have specific knowledge about the network
   indicating that sending datagrams to unvalidated addresses in a given
   range is not safe.

> **MAY**: Endpoints MAY choose to reduce the risk of request forgery by not
   including values from NEW_TOKEN frames in Initial packets or by only
   sending probing frames in packets prior to completing address
   validation.  Note that this does not prevent an attacker from using
   the Destination Connection ID field for an attack.

   Endpoints are not expected to have specific information about the
   location of servers that could be vulnerable targets of a request
   forgery attack.  However, it might be possible over time to identify
   specific UDP ports that are common targets of attacks or particular
> **MAY**: patterns in datagrams that are used for attacks.  Endpoints MAY
   choose to avoid sending datagrams to these ports or not send
   datagrams that match these patterns prior to validating the
> **MAY**: destination address.  Endpoints MAY retire connection IDs containing
   patterns known to be problematic without using them.

      |  Note: Modifying endpoints to apply these protections is more
      |  efficient than deploying network-based protections, as
      |  endpoints do not need to perform any additional processing when
      |  sending to an address that has been validated.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
