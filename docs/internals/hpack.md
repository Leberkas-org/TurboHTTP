# HPACK Header Compression

HPACK (RFC 7541) komprimiert HTTP/2 Header. Drei Techniken: **Static Table** (61 vordefinierte Header), **Dynamic Table** (kürzlich gesendete Header), **Huffman Encoding**.

## HPACK Subsystem

<ClientOnly>
  <LikeC4Diagram viewId="hpackSubsystem" :height="440" />
</ClientOnly>

Encoder und Decoder halten **synchronisierte Dynamic Tables**. Wenn eine Seite einen Header hinzufügt, muss die andere dasselbe tun.

## Encoding-Strategien

| Strategie | Wire Size | Wann |
|-----------|-----------|------|
| **Indexed** | 1 Byte | Name+Value in Static/Dynamic Table |
| **Literal + Indexing** | name + value | Häufig wiederholte Header |
| **Literal ohne Indexing** | name + value | Einmalige Header |
| **Never Indexed** | name + value | Sensitive Header (Authorization, Cookie) |

## Sensitive Headers

`Authorization`, `Cookie`, `Set-Cookie`, `Proxy-Authorization` werden automatisch als **Never Index** markiert — Schutz gegen CRIME/BREACH Side-Channel-Angriffe.

## Dynamic Table

FIFO Ring-Buffer mit Eviction:
- Eintragsgröße: `name.Length + value.Length + 32` Bytes
- Max-Größe via `SETTINGS_HEADER_TABLE_SIZE` (Default: 4096 Bytes)
- Älteste Einträge werden bei Überlauf evicted

## Huffman

Spart typischerweise 20-30% bei Header-Werten. Wird automatisch verwendet wenn die Huffman-Variante kleiner ist als das Literal.

## Tests

**419 Unit Tests** in `TurboHttp.Tests/RFC7541/` — die am gründlichsten getestete Komponente.
