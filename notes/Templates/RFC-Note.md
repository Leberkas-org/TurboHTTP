---
title: RFC Compliance Gap Template
description: >-
  Template for documenting RFC compliance gaps and limitations (distinct from
  RFC-Index.md)
tags:
  - template
  - rfc
  - gaps
aliases:
  - RFC Gap Template
  - Compliance Gap
---

# RFC {{rfc_number}}: {{gap_title}}

## Overview

Brief description of the compliance gap or limitation. This note documents **specific gaps within an RFC**, not the RFC overview (that goes in `RFC-Index.md`).

## Affected Section(s)

- RFC {{rfc_number}} Section X: {{section_name}} — [[../RFC{{rfc_number}}/{{rfc_number}}.md|See RFC Index]]

## Gap Description

### Current Behavior
What TurboHttp currently does (or doesn't do).

### RFC Requirement
What the RFC specifies or requires.

### Impact
- **On compliance**: Affects RFC {{rfc_number}} compliance score by ±X%
- **On users**: How this limitation affects users (if at all)
- **On performance**: Performance implications, if any

## Workaround

If a workaround exists, document it:
- Workaround approach
- Limitations of workaround

## Test Coverage

- Unit tests: {{X}} tests in `TurboHttp.Tests/RFC{{rfc_number}}/`
- Integration tests: {{Y}} tests in `TurboHttp.IntegrationTests/`
- Gap coverage: ✅ / 🔶 / ❌

## Priority

- **Critical** (blocks production)
- **High** (affects many users)
- **Medium** (affects some users)
- **Low** (edge case)

## Related Notes

- [[../RFC/00-RFC_STATUS_MATRIX|RFC Status Matrix]] — Overall compliance tracking
- [[../Architecture/Status/03-KNOWN_GAPS_AND_LIMITATIONS|All Known Gaps]] — Cross-RFC gap summary
- {{link to related RFC gap notes}}

## References

- [RFC {{rfc_number}} Section X](https://www.rfc-editor.org/rfc/rfc{{rfc_number}}#section-x) — RFC text
- [[../../Features/feature_name|Feature Plan]] — Related feature (if applicable)
- `{{file_path}}:{{line_number}}` — Code location
