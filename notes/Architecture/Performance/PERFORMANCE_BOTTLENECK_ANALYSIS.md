---
title: TurboHTTP Performance Bottleneck Analysis
date: '2026-04-08'
type: analysis
status: actionable
tags:
  - performance
  - bottlenecks
  - throughput
  - allocations
  - flow-control
---
# TurboHTTP Performance Bottleneck Analysis

> **Date:** 2026-04-08
> **Scope:** Full pipeline deep-dive — Encoding, Decoding, Transport, Flow Control, Memory/Allocations
> **Method:** 5 parallel code analysis agents covering all hot paths

---

## CRITICAL — Highest Impact

### 1. HPACK/QPACK Dynamic Table: LinkedList O(n) Lookup

Both dynamic tables use `LinkedList<T>` with linear search per header reference. For 100 headers this means **~5,050 pointer dereferences** per response.

| File | Lines | Issue |
|------|-------|-------|
| `Protocol/Http2/Hpack/HpackDecoder.cs` | 71-85 | `GetEntry()` — O(n) LinkedList walk per index |
| `Protocol/Http3/Qpack/QpackDynamicTable.cs` | 118-133 | `GetEntry()` — O(n) LinkedList walk per absolute index |
| `Protocol/Http3/Qpack/QpackEncoder.cs` | 509-550 | `FindDynamicExact()`/`FindDynamicName()` — linear search |

**Fix:** Replace with `List<T>` (index-based O(1)) or ring buffer with hash index.

---

### 2. HTTP/2 Request Body: Triple-Copy Pattern

A 10MB POST body gets **copied 3 times** before landing in frames:

| File | Line | Copy |
|------|------|------|
| `Protocol/Http2/Http2RequestEncoder.cs` | 70 | HttpContent → MemoryStream |
| `Protocol/Http2/Http2RequestEncoder.cs` | 74 | MemoryStream → `new byte[bodyLen]` |
| `Protocol/Http2/Http2RequestEncoder.cs` | 93-100 | byte[] → 16KB frame chunks |

**Impact:** ~7x memory overhead for large bodies.
**Fix:** Stream directly from HttpContent into frame chunks without intermediate buffers.

---

### 3. HTTP/3 Encoding: Allocation per Header

QPACK encoder allocates `Encoding.UTF8.GetBytes()` **per header field per request** (5-30 allocations/request):

| File | Lines |
|------|-------|
| `Protocol/Http3/Qpack/QpackEncoder.cs` | 247, 254, 493, 502 |
| `Protocol/Http3/Qpack/QpackEncoderInstructionWriter.cs` | 77, 113-114 |

**Fix:** `ArrayPool<byte>` with Span overload `GetBytes(string, Span<byte>)`.

---

### 4. Graph Materialization per Substream

`VersionDispatchStage` materializes the **entire engine pipeline** for every new endpoint group:

| File | Lines | Issue |
|------|-------|-------|
| `Streams/Stages/Internal/VersionDispatchStage.cs` | 112-121 | `SubFusingMaterializer` creates all stage logics from scratch |

**Impact:** 10 different endpoints = 10x full pipeline allocation (Encoder, Decoder, Correlation, Features).
**Fix:** Flow caching per (Version, Endpoint).

---

### 5. HTTP/3 QUIC: Sequential Stream Opening

`SemaphoreSlim(1)` serializes QUIC stream opening — destroys multiplexing benefit:

| File | Lines | Issue |
|------|-------|-------|
| `Transport/Quic/QuicConnectionManager.cs` | 54-76 | `_spawnLock.WaitAsync()` blocks concurrent stream creation |

**Fix:** Remove lock.

---

## HIGH — Significant Impact

### 6. HTTP/2 Flow Control: Receive Window Too Small

Default `initialRecvWindowSize = 65535` bytes — at 50ms RTT this caps at **max ~1.3 Mbps per stream**.

| File | Line |
|------|------|
| `Streams/Stages/Decoding/Http20ConnectionStage.cs` | 81 |

**Fix:** Default to 1MB+, adapt based on BDP (Bandwidth-Delay Product).

---

### 7. HTTP/2 Stream State Pool Too Small

`StatePoolCapacity = 32`, but `maxConcurrentStreams = 100`. At CL>32 states are not recycled → GC churn:

| File | Line |
|------|------|
| `Streams/Stages/Decoding/Http20ConnectionStage.cs` | 208 |

**Fix:** Use direct "maxConcurrentStreams".

---

### 8. HPACK/QPACK: Repeated UTF-8 GetByteCount Calls

`EntrySize()` calls `Encoding.UTF8.GetByteCount()` **multiple times** for the same header (Add, Eviction, CheckSize):

| File | Lines |
|------|-------|
| `Protocol/Http2/Hpack/HpackDecoder.cs` | 108, 215, 322 |
| `Protocol/Http3/Qpack/QpackDynamicTable.cs` | 164 |

**Fix:** Cache byte-length at insertion time (store in header struct).

---

### 9. HTTP/3 Frame Decoder: No Buffer Pooling

Every fragmented frame allocates `new byte[]` without ArrayPool:

| File | Lines | Issue |
|------|-------|-------|
| `Protocol/Http3/Http3FrameDecoder.cs` | 44, 62, 79 | `new byte[]` for combined/remainder |
| `Protocol/Http3/Http3FrameDecoder.cs` | 199, 204, 235 | `.ToArray()` for frame payloads |
| `Protocol/Http3/Http3ResponseDecoder.cs` | 123-149 | `List<byte[]>` body assembly with O(n²) copying |
| `Protocol/Http3/Qpack/QpackInstructionDecoder.cs` | 332 | `new byte[]` for combined buffer |

**Fix:** `ArrayPool<byte>.Shared.Rent()` + `Memory<byte>` slices instead of `.ToArray()`.

---

### 10. HTTP/1.0 Decoder: Excessive ToArray()

Every response parse allocates multiple times via `.ToArray()`:

| File | Lines |
|------|-------|
| `Protocol/Http10/Http10Decoder.cs` | 79, 111, 116, 141, 155, 165, 207, 247, 252 |
| `Protocol/Http10/Http10Decoder.cs` | 485 | `Combine()` — `new byte[]` without pooling |

---

### 11. HuffmanCodec: MemoryStream + ToArray()

Every encode/decode allocates MemoryStream and copies via `.ToArray()`:

| File | Lines |
|------|-------|
| `Protocol/HuffmanCodec.cs` | 110-112 | `new MemoryStream()` + `.ToArray()` in Encode |
| `Protocol/HuffmanCodec.cs` | 138 | `new MemoryStream()` in Decode |

**Fix:** Span-based with pre-sized buffer.

---

## MEDIUM — Noticeable Under Load

### 12. Batch Weight Too Conservative

`MaxBatchWeight = 65536` (64KB) — at high throughput causes too many scheduler ticks:

| File | Line |
|------|------|
| `Streams/Http20Engine.cs` | 16 |

**Fix:** 256KB-512KB for high-throughput, adaptive.

---

### 13. MemoryStream Allocations Scattered Everywhere

~9+ locations create `new MemoryStream()` without pooling:

| File | Context |
|------|---------|
| `Protocol/Http3/Http3RequestEncoder.cs:77` | Per-request body |
| `Protocol/Http10/Http10Encoder.cs:149` | Unknown-length body |
| `Protocol/Semantics/ContentEncodingEncoder.cs:52,63,74` | Compression |
| `Protocol/Semantics/ContentEncodingDecoder.cs:185` | Decompression |
| `Streams/Stages/Features/ContentEncodingBidiStage.cs:299-332` | Multiple instances |

**Fix:** `RecyclableMemoryStreamManager`.

---

### 14. Per-Request Collection Allocations

`new List<T>` / `new Dictionary<T,V>` in hot paths:

| File                                   | Lines   | What                                       |
| -------------------------------------- | ------- | ------------------------------------------ |
| `Protocol/Http2/Http2FrameDecoder.cs`  | 109     | `new List<Http2Frame>()` per decode        |
| `Protocol/Http3/Http3FrameDecoder.cs`  | 98      | `new List<Http3Frame>()` per decode        |
| `Protocol/Http2/Hpack/HpackDecoder.cs` | 193     | `new List<HpackHeader>()` per header block |
| `Protocol/Http3/Qpack/QpackDecoder.cs` | 95, 140 | `new List<(string,string)>()` per decode   |
| `Protocol/Cookies/CookieJar.cs`        | 112     | `new List<CookieEntry>()` per request      |

**Fix:** `ArrayPool`-backed lists.

---

### 15. TcpConnectionStage: Task.Run per Connection

Every TCP connection spawns `Task.Run()` for the inbound pump:

| File | Line |
|------|------|
| `Transport/Tcp/TcpConnectionStage.cs` | 523 |
| `Transport/Quic/QuicConnectionStage.cs` | 459 |

---

### 16. QPACK Encoder Instruction Blocking

When encoder instructions cannot be flushed, this **serializes all** subsequent requests:

| File | Lines |
|------|-------|
| `Streams/Stages/Encoding/Http30Request2FrameStage.cs` | 92-96 |

---

## LOW — Nice-to-Have

| # | Issue | File:Line |
|---|-------|-----------|
| 17 | `QpackStringCodec` allocates Huffman-Encode just to check length | `Qpack/QpackStringCodec.cs:29` |
| 18 | `DateTime.UtcNow` per connection in eviction loop | `ConnectionManagerActor.cs:306` |
| 19 | `GroupByRequestEndpointStage.RemoveDead()` allocates `List<int>` even when empty | `GroupByRequestEndpointStage.cs:159` |
| 20 | Socket buffer sizes not configurable | `IClientProvider.cs:100` |
| 21 | `HuffmanCodec._root` volatile instead of static initializer | `HuffmanCodec.cs:115` |
| 22 | NetworkBuffer pool unbounded (no cap) | `Messages.cs:80` |

---

## Top 5 Quick Wins (Effort vs Impact)

| # | Fix | Expected Impact | Effort |
|---|-----|-----------------|--------|
| 1 | HPACK/QPACK `LinkedList` → `List<T>` | **~30% faster header decode** | 2-3h |
| 2 | HTTP/2 body: direct streaming instead of triple-copy | **~7x less memory for POST** | 4-6h |
| 3 | QPACK Encoder: `stackalloc`/`ArrayPool` instead of `GetBytes()` | **~20-30 fewer allocs/request** | 2-3h |
| 4 | HTTP/3 FrameDecoder: `ArrayPool` instead of `new byte[]` | **GC pressure significantly reduced** | 1-2h |
| 5 | Receive window → 1MB+ | **Throughput x10+ at latency >10ms** | 30min |

---

## Next Steps

- [ ] Create feature plans for top 5 quick wins
- [ ] Run BenchmarkDotNet baselines before changes
- [ ] Implement fixes in priority order
- [ ] Re-benchmark after each fix to measure actual impact
