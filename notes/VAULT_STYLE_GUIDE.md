---
title: Vault Style Guide
description: Unified structure, frontmatter standards, and conventions for all notes
tags: [meta, guide, style]
---

# Obsidian Vault Style Guide

**Last Updated**: 2026-03-26

This guide ensures consistency across the entire TurboHttp knowledge base.

---

## 1. Frontmatter Standard

Every note **MUST** include frontmatter with these fields:

```yaml
---
title: Note Title (matches # heading)
description: One-line description of what this note contains
tags: [category, subcategory, topic]
aliases: [alt-name-1, alt-name-2]  # Optional: alternative names for wikilinks
---
```

### Frontmatter Fields

| Field | Required | Type | Example |
|-------|----------|------|---------|
| `title` | ✅ | String | `"Layered Architecture"` |
| `description` | ✅ | String | `"7-layer design with client to transport"` |
| `tags` | ✅ | Array | `[architecture, design, patterns]` |
| `aliases` | 🟢 | Array | `[LayerDesign, ArchOverview]` |
| `created` | 🟡 | Date | `2026-03-26` (auto-filled by template) |
| `updated` | 🟡 | Date | `2026-03-26` (keep current) |

### Tag Convention

Tags follow a **hierarchical structure**:

```
[primary-category, subcategory, specific-topic]

Examples:
- [architecture, layers, design]
- [rfc, http2, compression]
- [testing, unit, decoder]
- [gaps, critical, http3]
- [patterns, stage, bidi]
```

**Primary Categories**:
- `architecture` — Design patterns, system structure
- `rfc` — RFC compliance, standards
- `patterns` — Code patterns, best practices
- `gaps` — Known issues, limitations
- `testing` — Test organization, helpers
- `features` — Features, roadmap
- `guide` — How-tos, tutorials
- `reference` — Reference material
- `meta` — Meta notes (this guide, index)

---

## 2. Heading Hierarchy (H1 → H6)

```markdown
# Main Title (H1)
Only ONE per note, matches frontmatter `title`

## Section (H2)
Major sections within the note

### Subsection (H3)
Detailed breakdowns

#### Detail (H4)
Code examples, lists, tables

##### Rare (H5)
Use sparingly for deep nesting

###### Very Rare (H6)
Avoid if possible
```

### Rules

1. ✅ Every note starts with `# Title` (H1)
2. ✅ H1 matches frontmatter `title` field
3. ✅ No skipping levels (H1 → H2 → H3, not H1 → H3)
4. ✅ Section titles are descriptive (not "Overview", use "Architecture Overview")
5. ✅ Maximum nesting: H4 for most content, H5 for exceptions

---

## 3. Consistent Formatting

### Emphasis & Text

```markdown
**Bold** — Important concepts, emphasis
*Italic* — Soft emphasis, names
`code` — Inline code, class names, methods
`CodeBlock` — Type names
~~strikethrough~~ — Deprecated, removed items
```

### Lists

```markdown
# Unordered Lists
- Item 1
- Item 2
  - Nested item
  - Another nested

# Ordered Lists
1. First step
2. Second step
   1. Sub-step
   2. Another sub-step

# Task Lists
- [x] Completed task
- [ ] Pending task
- [~] In progress
```

### Code Blocks

```markdown
​```language
// Always specify language (csharp, bash, yaml, etc.)
var example = new(); 
​```
```

**Languages**:
- `csharp` — C# code
- `bash` — Shell commands
- `yaml` — YAML config
- `json` — JSON data
- `markdown` — Markdown examples
- `text` — Plain text, output

### Tables

```markdown
| Column 1 | Column 2 | Status |
|----------|----------|--------|
| Left | Center | Right |
| Data | Data | ✅ ✓ |
```

**Rules**:
- ✅ Use pipes for alignment
- ✅ Include header separator row
- ✅ Use status emojis (✅ ✓ ❌ 🔶 🟡 🟢)

### Blockquotes & Callouts

```markdown
> **Note**: Important callout text

> ⚠️ **Warning**: Something to be careful about

> 💡 **Tip**: Helpful suggestion

> 📌 **Reference**: Link to external resource
```

---

## 4. Linking & References

### Internal Links (WikiLinks)

```markdown
[[note-name|Display Text]]          # Link to another note
[[note-name#heading|Specific Section]]  # Link to section
[[note-name]]                       # Link with note title as text
```

**Rules**:
- ✅ Use `[[relative-path|Display Text]]` format
- ✅ Relative paths from vault root: `[[Architecture/01-LAYERED|Layer Design]]`
- ✅ Section anchors: `[[RFC/00-RFC_STATUS_MATRIX#HTTP/2|HTTP/2 Compliance]]`

### External Links

```markdown
[Link Text](https://example.com)
[RFC 9113](https://www.rfc-editor.org/rfc/rfc9113)
```

### Reference Links (Footer)

Use when referencing same URLs multiple times:

```markdown
See [[RFC/00-RFC_STATUS_MATRIX|RFC Status Matrix]] and 
[[Architecture/01-LAYERED_ARCHITECTURE|Layered Architecture]].

For external refs:
- [RFC Editor](https://www.rfc-editor.org/) — All RFCs
- [HTTP/2 Spec](https://www.rfc-editor.org/rfc/rfc9113)
```

---

## 5. Status Indicators & Emojis

### Task/Completion Status

| Symbol | Meaning | Use |
|--------|---------|-----|
| ✅ | Complete | Features, phases, tests |
| 🔶 | Partial/In-Progress | Partial implementation |
| 🟠 | High Priority | Urgent work needed |
| 🟡 | Medium Priority | Should do soon |
| 🟢 | Low Priority | Nice-to-have |
| ❌ | Missing/Failed | Not implemented, broken |
| 🔴 | Critical | Blocks production |

### Category Emojis (use consistently)

| Emoji | Category |
|-------|----------|
| 📁 | Folder/Section |
| 📄 | File/Document |
| 📝 | Notes/Documentation |
| 🏗️ | Architecture |
| 🧪 | Testing |
| ✨ | Features |
| 🐛 | Bugs/Issues |
| 🔒 | Security |
| ⚡ | Performance |
| 🚀 | Release/Deployment |

### Consistency Rule

✅ Use emojis **consistently** — same emoji for same concept across all notes

---

## 6. Code Examples

### CSharp Examples

```csharp
// Bad: No context
var pool = new ConnectionPool();

// Good: Full example with context
public sealed class ConnectionPoolExample
{
    public async Task AcquireConnectionAsync()
    {
        var pool = new ConnectionPool();
        var lease = await pool.AcquireAsync(
            new RequestEndpoint("example.com", 443),
            TcpOptions.Default);
        // Use lease...
    }
}
```

**Rules**:
- ✅ Complete, runnable examples
- ✅ Comments explaining intent
- ✅ Variable names are descriptive
- ✅ Format: 4-space indentation (Allman style)

### Bash Examples

```bash
# Comment explaining the command
dotnet build --configuration Release ./src/TurboHttp.sln

# Running tests
dotnet test ./src/TurboHttp.Tests/ -- --filter-namespace "TurboHttp.Tests.RFC9113"
```

---

## 7. Section Templates by Note Type

### Architecture Decision Record (ADR)

```markdown
# [ADR-001: Decision Title]

## Status
[Proposed | Accepted | Deprecated | Superseded by ADR-NNN]

## Context
Why was this decision needed? What problem does it solve?

## Decision
What was decided? Why this choice?

## Consequences
**Positive**:
- Benefit 1
- Benefit 2

**Negative**:
- Trade-off 1
- Trade-off 2

## Alternatives Considered
- Alternative A — Why not chosen
- Alternative B — Why not chosen

## References
- Related notes/RFCs/specs
```

### RFC Compliance Note

```markdown
# RFC NNNN (Protocol Name)

## Status
✅ Complete | 🔶 Partial | ❌ Missing

## Overview
Brief description of RFC scope and requirements.

## Implemented ✅
- Feature 1 — Status
- Feature 2 — Status

## Gaps 🔶
- Missing feature — Impact
- Limitation — Workaround

## Test Coverage
- Unit tests: X tests in `RFC####/` folder
- Integration tests: Y tests in StreamTests
- Compliance: Z%

## Related Sections
- [[RFC/00-RFC_STATUS_MATRIX|Status Matrix]]
- [[Architecture/03-KNOWN_GAPS_AND_LIMITATIONS|Limitations]]
```

### Pattern/Best Practice Note

```markdown
# Pattern: [Pattern Name]

## When to Use
Situations where this pattern applies.

## How It Works
Explanation of the pattern.

## Example
```csharp
// Code example
```

## Pros & Cons
| Pros | Cons |
|------|------|
| Benefit 1 | Trade-off 1 |
| Benefit 2 | Trade-off 2 |

## Related Patterns
- [[Pattern Name 2]]
- [[Pattern Name 3]]

## References
- External resources
```

---

## 8. RFC Index Template

Every RFC tracked in the vault **MUST** have an index file at `RFC/RFCXXXX/RFCXXXX.md` following the standard template (`Templates/RFC-Index.md`). This ensures consistent navigation, compliance tracking, and cross-referencing across all protocol specifications.

### Required Frontmatter

```yaml
---
title: "RFC XXXX — Protocol Name"
rfc_number: XXXX
description: "One-line description of RFC scope and TurboHttp relevance"
tags: [rfc, rfcXXXX, protocol-category]
---
```

| Field | Required | Notes |
|-------|----------|-------|
| `title` | ✅ | Format: `"RFC XXXX — Protocol Name"` |
| `rfc_number` | ✅ | Integer RFC number (e.g., `1945`, `9113`) |
| `description` | ✅ | One-line summary of RFC scope |
| `tags` | ✅ | Must include `rfc` and `rfcXXXX`; add protocol category tags |

### Required Sections (in order)

1. **Quick Reference** — Table with Compliance Score, Implementation Status, Implementation Path, Unit/Stream Test Files, Key Gaps
2. **Core Concepts** — Bullet list linking to key section files with brief descriptions
3. **Implementation Notes** — Sub-tables for Encoder, Decoder, Stages, and Tests with file paths
4. **Sections** — Complete table of all section files with `#`, Section name, WikiLink, and Status badge
5. **Dependencies** — Table showing RFC-to-RFC relationships (Depends on / Used by)
6. **See Also** — Links to RFC Status Matrix and Known Gaps
7. **Full RFC Document** (optional) — Embedded raw RFC text (preserved from original import)

### Status Badges for Sections

| Badge | Meaning |
|-------|---------|
| ✅ | Section complete with meaningful content |
| 🔶 | Section exists but sparse or partially filled |
| 🟡 | Section is a stub or placeholder |

### Reference Implementation

See `RFC/RFC1945/RFC1945.md` for a fully populated example of the RFC Index template applied to HTTP/1.0.

### WikiLink Patterns in RFC Index Files

```markdown
# Section links (relative to the RFC index file)
[[sections/NN_topic_name|Display Name]]

# Cross-RFC links
[[../RFCXXXX/RFCXXXX|RFC XXXX — Protocol Name]]

# Vault-level links
[[../00-RFC_STATUS_MATRIX|RFC Status Matrix]]
[[../../Architecture/03-KNOWN_GAPS_AND_LIMITATIONS|Known Gaps]]
```

---

## 9. CSS Custom Classes

Use Obsidian CSS snippets for visual consistency:

```markdown
<!-- Callout Block -->
> 💡 **Tip**: Use this for helpful hints

<!-- Code Block with Language -->
​```csharp
var code = "highlighted";
​```

<!-- Status Badge (using text) -->
Status: ✅ Complete | 🔶 Partial | ❌ Missing
```

### Available CSS Classes (to be added via snippets)

```css
/* Define in .obsidian/snippets/turbohttp-style.css */
.note-architecture { color: #0066cc; }
.note-rfc { color: #cc6600; }
.note-testing { color: #009900; }
.note-gap { color: #cc0000; }
.status-complete { color: #00aa00; }
.status-partial { color: #ffaa00; }
.status-missing { color: #dd0000; }
```

---

## 10. File Organization

```
notes/
├── 00-Index.md                           # Main hub (this frontmatter)
├── VAULT_STYLE_GUIDE.md                  # This file (meta)
├── Architecture/                         # Design decisions, patterns
│   ├── 01-LAYERED_ARCHITECTURE.md
│   ├── 02-STAGE_PATTERNS.md
│   ├── 03-KNOWN_GAPS_AND_LIMITATIONS.md
│   └── 04-CURRENT_STATE_SUMMARY.md
├── RFC/                                  # RFC compliance tracking
│   ├── 00-RFC_STATUS_MATRIX.md          # Master summary
│   ├── RFC1945_*.md                     # HTTP/1.0
│   ├── RFC9112_*.md                     # HTTP/1.1
│   ├── RFC9113_*.md                     # HTTP/2
│   ├── RFC9114_*.md                     # HTTP/3
│   └── ...
├── Features/                             # Feature planning
│   └── [Linked from .maggus/features/]
├── Sessions/                             # Session logs (optional)
│   └── Session-YYYY-MM-DD.md
├── Debugging/                            # Bug investigations (git-ignored)
│   └── [Temporary investigation notes]
└── Templates/                            # Note templates
    ├── ADR.md
    ├── RFC-Note.md
    ├── Session-Log.md
    └── Bug-Investigation.md
```

---

## 11. Quality Checklist Before Publishing

Before considering a note "complete":

- [ ] Frontmatter present (title, description, tags)
- [ ] Single H1 heading matching frontmatter title
- [ ] Proper heading hierarchy (no skipped levels)
- [ ] All code blocks have language specified
- [ ] External links are complete URLs
- [ ] Internal links use `[[path|Display Text]]` format
- [ ] Status emojis used consistently
- [ ] Tables formatted with pipes
- [ ] No bare URLs (use markdown link syntax)
- [ ] Spelling & grammar checked
- [ ] Related notes linked at bottom
- [ ] Total length reasonable (split if > 2000 words)

---

## 12. Updates & Maintenance

### When to Update Frontmatter

- ✅ Update `description` if note scope changes
- ✅ Update `tags` if new categories emerge
- 🟢 Keep `created` date immutable (original creation)
- 🟡 Update `updated` field occasionally (not needed daily)

### When to Refactor Notes

- If a note exceeds **2000 words** → Split into multiple focused notes
- If new patterns emerge → Extract to new pattern note
- If gaps accumulate → Create new limitations/roadmap note
- If structure changes → Update index links

### Vault Statistics

Run periodically:
- Total notes: Target 30-50 (focused, not bloated)
- Average note length: Target 500-800 words
- Link density: Target 3-5 outbound links per note
- Tag usage: Target 5-10 unique tags

---

## 13. Examples

### ✅ Good Note Structure

```markdown
---
title: GraphStage Patterns
description: Common patterns for building Akka.Streams GraphStages
tags: [patterns, akka, stages, design]
aliases: [Stage Patterns, GraphStage Design]
---

# GraphStage Patterns

Brief intro paragraph explaining what this covers.

## Pattern 1: Encoder Stage

### When to Use
Description...

### How It Works
Explanation...

### Example
​```csharp
// Code
​```

## Pattern 2: Decoder Stage
...

## See Also
- [[Architecture/02-STAGE_PATTERNS|Stage Conventions]]
- [[Patterns/Naming|Naming Patterns]]
```

### ❌ Bad Note Structure

```markdown
# My Notes

Some random text without frontmatter.

## Things I learned

Random bullet points, no context.

## Code snippet

var x = 5; // No language, no explanation
```

---

## CSS Snippet Template

Save this as `.obsidian/snippets/turbohttp-style.css`:

```css
/* TurboHttp Vault Styling */

/* Headings */
.cm-header { font-weight: 600; }
h1 { border-bottom: 3px solid #0066cc; padding-bottom: 8px; }
h2 { border-left: 4px solid #0066cc; padding-left: 12px; }

/* Code blocks */
code {
    background-color: #f5f5f5;
    padding: 2px 6px;
    border-radius: 3px;
}

/* Tags */
.tag { 
    background-color: #e8f0ff;
    color: #0066cc;
    padding: 2px 8px;
    border-radius: 12px;
}

/* Internal links */
.internal-link {
    color: #0066cc;
    text-decoration: underline;
}

/* Blockquotes */
blockquote {
    border-left: 4px solid #ffaa00;
    padding-left: 16px;
    color: #666;
}

/* Status indicators */
.status-complete { color: #00aa00; font-weight: 600; }
.status-partial { color: #ffaa00; font-weight: 600; }
.status-missing { color: #dd0000; font-weight: 600; }
```

---

## Quick Reference

| Task | Template |
|------|----------|
| New architecture decision | Use ADR template |
| Document RFC compliance | Use RFC-Note template |
| Session log | Use Session-Log template |
| Bug investigation | Use Bug-Investigation template |
| Any note | Add frontmatter + single H1 |

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2026-03-26 | Initial style guide |

---

## Questions?

See [[00-Index|Main Index]] for vault overview.
