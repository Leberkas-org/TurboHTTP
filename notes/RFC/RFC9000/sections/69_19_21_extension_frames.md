---
title: "19.21.  Extension Frames"
rfc_number: 9000
rfc_section: "19.21"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 19.21: Extension Frames — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, extension_frames]
---

# 19.21.  Extension Frames


   QUIC frames do not use a self-describing encoding.  An endpoint
   therefore needs to understand the syntax of all frames before it can
   successfully process a packet.  This allows for efficient encoding of
   frames, but it means that an endpoint cannot send a frame of a type
   that is unknown to its peer.

> **MUST**: An extension to QUIC that wishes to use a new type of frame MUST
   first ensure that a peer is able to understand the frame.  An
   endpoint can use a transport parameter to signal its willingness to
   receive extension frame types.  One transport parameter can indicate
   support for one or more extension frame types.

   Extensions that modify or replace core protocol functionality
   (including frame types) will be difficult to combine with other
   extensions that modify or replace the same functionality unless the
   behavior of the combination is explicitly defined.  Such extensions
> **SHOULD**: SHOULD define their interaction with previously defined extensions
   modifying the same protocol components.

> **MUST**: Extension frames MUST be congestion controlled and MUST cause an ACK
   frame to be sent.  The exception is extension frames that replace or
   supplement the ACK frame.  Extension frames are not included in flow
   control unless specified in the extension.

   An IANA registry is used to manage the assignment of frame types; see
   Section 22.4.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
