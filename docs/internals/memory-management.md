# Memory Management

TurboHttp minimiert Allokationen auf dem Hot Path. Das Diagramm zeigt den Zero-Copy Datenpfad von TCP bis zur Response.

## Zero-Copy Data Path

<ClientOnly>
  <LikeC4Diagram viewId="dataPath" :height="480" />
</ClientOnly>

**Grün** = Datenpfad (keine Actor-Hops). **Amber** = Lifecycle-Actors (nur Verwaltung).

## Datenfluss: TCP → Response

```
TCP Socket → PipeWriter → ClientByteMover → Channel → ConnectionStage → DecoderStage → HttpResponseMessage
```

**1 Kopie total** (TCP Kernel → PipeWriter). Alles danach ist Slicing von `ReadOnlyMemory<byte>`.

## Schlüssel-Typen

| Typ | Verwendung |
|-----|-----------|
| `Span<T>` | Synchrones Encoding — Stack-gebunden, keine Heap-Allokation |
| `ReadOnlyMemory<byte>` | Async Datenpfad — Body ist Slice des TCP-Buffers |
| `IBufferWriter<byte>` | Encoder-Output — Caller kontrolliert Speicher |
| `IMemoryOwner<byte>` | Geliehener Buffer — **muss disposed werden** |
| `ArrayPool<byte>` | Temporäre Buffer für Header-Parsing |

## HTTP/2 Frame-Serialisierung

Jeder Frame berechnet `SerializedSize` vor dem Schreiben — kein Resize, kein Waste:

```csharp
frame.SerializedSize  // → 9 + payload.Length
frame.WriteTo(ref span); // Direktes Schreiben in pre-allozierten Buffer
```
