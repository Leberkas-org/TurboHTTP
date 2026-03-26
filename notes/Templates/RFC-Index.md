---
title: "RFC XXXX — Protocol Name"
rfc_number: XXXX
description: "One-line description of the RFC scope and TurboHttp relevance"
tags: [rfc, rfcXXXX, protocol-category]
---

# RFC XXXX — Protocol Name

**Official RFC**: [RFC XXXX](https://www.rfc-editor.org/rfc/rfcXXXX)

## Quick Reference

| Metric | Value |
|--------|-------|
| **Compliance Score** | XX/100 |
| **Implementation Status** | ✅ Complete / 🔶 Partial / 🟡 Draft / ❌ Missing |
| **Implementation Path** | `TurboHttp/Protocol/RFCXXXX/` |
| **Unit Test Files** | `TurboHttp.Tests/RFCXXXX/` — N files, M tests |
| **Stream Test Files** | `TurboHttp.StreamTests/RFCXXXX/` — N files |
| **Key Gaps** | Brief summary of main gaps |

## Core Concepts

Key ideas from this RFC, with links to section files:

- [[sections/NN_topic|Topic Name]] — brief description
- [[sections/NN_topic|Topic Name]] — brief description

## Implementation Notes

### Encoder

| File | Purpose |
|------|---------|
| `Protocol/RFCXXXX/EncoderFile.cs` | Description |

### Decoder

| File | Purpose |
|------|---------|
| `Protocol/RFCXXXX/DecoderFile.cs` | Description |

### Stages

| File | Purpose |
|------|---------|
| `Streams/Stages/Encoding/StageFile.cs` | Description |
| `Streams/Stages/Decoding/StageFile.cs` | Description |

### Tests

| Location | Count | Focus |
|----------|-------|-------|
| `TurboHttp.Tests/RFCXXXX/` | N tests | Protocol compliance |
| `TurboHttp.StreamTests/RFCXXXX/` | N tests | Stage behaviour |

## Sections

| # | Section | File | Status |
|---|---------|------|--------|
| 00 | Preamble | [[sections/00_preamble]] | ✅ |
| 01 | Section Title | [[sections/NN_name]] | ✅ / 🔶 / 🟡 |

## Dependencies

| Direction | RFC | Relationship |
|-----------|-----|--------------|
| **Depends on** | [[../RFCXXXX/RFCXXXX\|RFC XXXX]] | Description |
| **Used by** | [[../RFCXXXX/RFCXXXX\|RFC XXXX]] | Description |

## See Also

- [[../00-RFC_STATUS_MATRIX|RFC Status Matrix]]
- [[../../Architecture/03-KNOWN_GAPS_AND_LIMITATIONS|Known Gaps]]
