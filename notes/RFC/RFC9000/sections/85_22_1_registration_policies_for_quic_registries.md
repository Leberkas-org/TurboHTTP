---
title: "22.1.  Registration Policies for QUIC Registries"
rfc_number: 9000
rfc_section: "22.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 22.1: Registration Policies for QUIC Registries — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, registration_policies_for_quic_registries]
---

# 22.1.  Registration Policies for QUIC Registries



   This document establishes several registries for the management of
   codepoints in QUIC.  These registries operate on a common set of
   policies as defined in Section 22.1.

## 22.1.  Registration Policies for QUIC Registries

   All QUIC registries allow for both provisional and permanent
   registration of codepoints.  This section documents policies that are
   common to these registries.

### 22.1.1.  Provisional Registrations

   Provisional registrations of codepoints are intended to allow for
   private use and experimentation with extensions to QUIC.  Provisional
   registrations only require the inclusion of the codepoint value and
   contact information.  However, provisional registrations could be
   reclaimed and reassigned for another purpose.

   Provisional registrations require Expert Review, as defined in
   Section 4.5 of [RFC8126].  The designated expert or experts are
   advised that only registrations for an excessive proportion of
   remaining codepoint space or the very first unassigned value (see
   Section 22.1.2) can be rejected.

   Provisional registrations will include a Date field that indicates
   when the registration was last updated.  A request to update the date
   on any provisional registration can be made without review from the
   designated expert(s).

   All QUIC registries include the following fields to support
   provisional registration:

   Value:  The assigned codepoint.
   Status:  "permanent" or "provisional".
   Specification:  A reference to a publicly available specification for
      the value.
   Date:  The date of the last update to the registration.
   Change Controller:  The entity that is responsible for the definition
      of the registration.
   Contact:  Contact details for the registrant.
   Notes:  Supplementary notes about the registration.

> **MAY**: Provisional registrations MAY omit the Specification and Notes
   fields, plus any additional fields that might be required for a
   permanent registration.  The Date field is not required as part of
   requesting a registration, as it is set to the date the registration
   is created or updated.

### 22.1.2.  Selecting Codepoints

> **SHOULD**: New requests for codepoints from QUIC registries SHOULD use a
   randomly selected codepoint that excludes both existing allocations
   and the first unallocated codepoint in the selected space.  Requests
> **MAY**: for multiple codepoints MAY use a contiguous range.  This minimizes
   the risk that differing semantics are attributed to the same
   codepoint by different implementations.

   The use of the first unassigned codepoint is reserved for allocation
   using the Standards Action policy; see Section 4.9 of [RFC8126].  The
   early codepoint assignment process [EARLY-ASSIGN] can be used for
   these values.

   For codepoints that are encoded in variable-length integers
   (Section 16), such as frame types, codepoints that encode to four or
> **SHOULD**: eight bytes (that is, values 2^14 and above) SHOULD be used unless
   the usage is especially sensitive to having a longer encoding.

> **MAY**: Applications to register codepoints in QUIC registries MAY include a
   requested codepoint as part of the registration.  IANA MUST allocate
   the selected codepoint if the codepoint is unassigned and the
   requirements of the registration policy are met.

### 22.1.3.  Reclaiming Provisional Codepoints

   A request might be made to remove an unused provisional registration
   from the registry to reclaim space in a registry, or a portion of the
   registry (such as the 64-16383 range for codepoints that use
> **SHOULD**: variable-length encodings).  This SHOULD be done only for the
   codepoints with the earliest recorded date, and entries that have
> **SHOULD NOT**: been updated less than a year prior SHOULD NOT be reclaimed.

> **MUST**: A request to remove a codepoint MUST be reviewed by the designated
   experts.  The experts MUST attempt to determine whether the codepoint
   is still in use.  Experts are advised to contact the listed contacts
   for the registration, plus as wide a set of protocol implementers as
   possible in order to determine whether any use of the codepoint is
   known.  The experts are also advised to allow at least four weeks for
   responses.

   If any use of the codepoints is identified by this search or a
> **MUST NOT**: request to update the registration is made, the codepoint MUST NOT be
   reclaimed.  Instead, the date on the registration is updated.  A note
   might be added for the registration recording relevant information
   that was learned.

   If no use of the codepoint was identified and no request was made to
> **MAY**: update the registration, the codepoint MAY be removed from the
   registry.

   This review and consultation process also applies to requests to
   change a provisional registration into a permanent registration,
   except that the goal is not to determine whether there is no use of
   the codepoint but to determine that the registration is an accurate
   representation of any deployed usage.

### 22.1.4.  Permanent Registrations

   Permanent registrations in QUIC registries use the Specification
   Required policy (Section 4.6 of [RFC8126]), unless otherwise
   specified.  The designated expert or experts verify that a
   specification exists and is readily accessible.  Experts are
   encouraged to be biased towards approving registrations unless they
   are abusive, frivolous, or actively harmful (not merely aesthetically
   displeasing or architecturally dubious).  The creation of a registry
> **MAY**: MAY specify additional constraints on permanent registrations.

> **MAY**: The creation of a registry MAY identify a range of codepoints where
   registrations are governed by a different registration policy.  For
   instance, the "QUIC Frame Types" registry (Section 22.4) has a
   stricter policy for codepoints in the range from 0 to 63.

   Any stricter requirements for permanent registrations do not prevent
   provisional registrations for affected codepoints.  For instance, a
   provisional registration for a frame type of 61 could be requested.

> **MUST**: All registrations made by Standards Track publications MUST be
   permanent.

   All registrations in this document are assigned a permanent status
   and list a change controller of the IETF and a contact of the QUIC
   Working Group (quic@ietf.org).

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
