# Test Infrastructure Audit: ActorSystem Tests Exercising Pure Protocol Logic

## Summary

This audit identifies StreamTests in `Http10/` and `Http11/` that spin up a full Akka
ActorSystem + Materializer purely to test protocol encoding/decoding logic. These tests
create an `ActorSystem` per test class (via `StreamTestBase` or `EngineTestBase`) even
though the tests never exercise actor messaging, supervision, or backpressure behaviour.

**Base classes and their cost:**
- `StreamTestBase` extends `Akka.TestKit.Xunit2.TestKit` — creates `ActorSystem` + `IMaterializer`
- `EngineTestBase` extends `TestKit` — creates `ActorSystem` + `IMaterializer` + `EngineFakeConnectionStage`

**Measured overhead:** ~342 ms per test class for `ActorSystem.Create()` and
`CoordinatedShutdown`. Plain `[Fact]` tests calling the encoder/decoder directly execute in
< 1 ms (see conversion example below).

---

## Audit Table: Http10/

| File | Tests | Uses Actor Behaviour? | What It Actually Tests | Recommendation |
|------|-------|-----------------------|------------------------|----------------|
| `Http10EncoderStageTests.cs` | 6 | **No** — `Source.Single → EncoderStage → Sink.Seq` | Request-line format, headers, body encoding | **Convert** — call `Http10Encoder.Encode()` directly |
| `Http10EncoderStageRfcTests.cs` | 5 | **No** — same `Source → Stage → Sink` pattern | Request-line, Host header, Content-Length | **Convert** — call `Http10Encoder.Encode()` directly |
| `Http10DecoderStageTests.cs` | 5 | **No** — `Source.From → DecoderStage → Sink.First` | Status-line, headers, body, fragmentation | **Convert** — call `Http10Decoder.TryDecode()` directly |
| `Http10DecoderStageRfcTests.cs` | 5 | **No** — same decode pattern | Status-line, headers, body, connection-close | **Convert** — call `Http10Decoder.TryDecode()` directly |
| `Http10StageRoundTripMethodTests.cs` | 5 | **No** — encode then decode, no shared stream | GET/POST/HEAD/DELETE/PUT round-trip | **Convert** — call encoder + decoder directly |
| `Http10StageRoundTripHeaderBodyTests.cs` | 5 | **No** — encode then decode, independent calls | Empty/large/binary body, custom headers | **Convert** — call encoder + decoder directly |
| `Http10StageTcpFragmentationTests.cs` | 5 | **No** — decoder stage with multiple input chunks | TCP fragment reassembly | **Convert** — call decoder with cumulative input |
| `Http10EngineRfcRoundTripTests.cs` | 5 | **Partially** — uses `EngineFakeConnectionStage` graph | Full engine pipeline: encoder→fake-TCP→decoder→correlation | **Keep** — tests stream wiring and correlation stage interaction |

### Http10 Summary
- **36 tests** that exercise only encoding/decoding → candidates for conversion
- **5 tests** that exercise stream pipeline behaviour → keep as stream tests

---

## Audit Table: Http11/

| File | Tests | Uses Actor Behaviour? | What It Actually Tests | Recommendation |
|------|-------|-----------------------|------------------------|----------------|
| `Http11EncoderStageTests.cs` | 5 | **No** — `Source.Single → EncoderStage → Sink.Seq` | Request-line, Host, body framing, hop-by-hop | **Convert** — call `Http11Encoder.Encode()` directly |
| `Http11EncoderStageRfcTests.cs` | 6 | **No** — same `Source → Stage → Sink` pattern | Request-line, Host authority, Content-Length | **Convert** — call `Http11Encoder.Encode()` directly |
| `Http11DecoderStageTests.cs` | 6 | **No** — `Source.From → DecoderStage → Sink.First/Seq` | Status-line, body, chunked, pipelining, fragmentation | **Convert** — call `Http11Decoder.TryDecode()` directly |
| `Http11DecoderStageChunkedRfcTests.cs` | 13 | **No** — same decode pattern | Single/multi chunk, terminator, extensions, trailers | **Convert** — call `Http11Decoder.TryDecode()` directly |
| `Http11StageFragmentationTests.cs` | 11 | **No** — decoder with fragmented input | TCP fragmentation: chunked, Content-Length, 1-byte | **Convert** — call decoder with cumulative input |
| `Http11BatchEncodingTests.cs` | 8 | **Partially** — 5 are pure unit tests (`BatchConsolidate`), 2 use `Source → Batch → Sink` | Batch consolidation logic + stream batching | **Partial** — 5 already plain; move 2 stream tests only if batch DSL can be tested without Materializer |
| `Http11ResponseCorrelationTests.cs` | 4 | **Yes** — full engine pipeline with FIFO ordering | Request-response correlation through engine | **Keep** — tests pipeline ordering |
| `Http11StageConnectionMgmtTests.cs` | 5 | **Yes** — multiple requests through engine, connection lifecycle | Connection: close, keep-alive, chunked+keep-alive | **Keep** — tests connection management through stream lifecycle |
| `Http11StageRoundTripPipelineTests.cs` | 5 | **Yes** — 10 requests through engine, FIFO guarantee | Pipelining: FIFO ordering, mixed methods | **Keep** — tests pipelining behaviour |
| `Http11StageStatusCodeTests.cs` | 5 | **Yes** — full engine pipeline per status code | Status code propagation through engine | **Keep** — tests full pipeline; but could convert since it only checks StatusCode |
| `Http11EngineRfcRoundTripTests.cs` | 5 | **Yes** — full engine pipeline | Chunked request+response, FIFO, Host header, hop-by-hop | **Keep** — tests engine wiring |
| `Http1XCorrelationStageTests.cs` | 9 | **Yes** — `GraphDsl.Create` with timing, buffering | Correlation stage: FIFO, buffering, timing, pending requests, StreamAcquireItem | **Keep** — tests GraphStage timing/backpressure semantics |

### Http11 Summary
- **41 tests** that exercise only encoding/decoding → candidates for conversion
- **2 tests** in `Http11BatchEncodingTests.cs` → partial candidates (stream batch DSL)
- **33 tests** that exercise stream pipeline behaviour → keep as stream tests

---

## Overall Summary

| Category | Test Count | Action |
|----------|------------|--------|
| Pure encode/decode via `Source → Stage → Sink` | **77** | Convert to plain `[Fact]` calling encoder/decoder directly |
| Stream batch DSL tests | **2** | Evaluate individually |
| Full engine pipeline / correlation / timing | **38** | Keep as stream tests |
| **Total** | **117** | |

**Conversion would eliminate ~77 ActorSystem instantiations**, saving approximately
77 × ~300 ms = **~23 seconds of cumulative overhead** in CI.

---

## Conversion Pattern

### Before (StreamTest — requires ActorSystem)

```csharp
public sealed class Http10EncoderStageTests : StreamTestBase  // ActorSystem created here
{
    private async Task<string> EncodeAsync(HttpRequestMessage request)
    {
        var chunks = await Source.Single(request)
            .Via(Flow.FromGraph(new Http10EncoderStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        var sb = new StringBuilder();
        foreach (var item in chunks)
        {
            var data = (DataItem)item;
            sb.Append(Encoding.Latin1.GetString(data.Memory.Memory.Span[..data.Length]));
            data.Memory.Dispose();
        }
        return sb.ToString();
    }

    [Fact(Timeout = 10_000)]
    public async Task ST_10_ENC_001_RequestLine_Format()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/index.html")
        {
            Version = HttpVersion.Version10
        };
        var raw = await EncodeAsync(request);
        Assert.StartsWith("GET /index.html HTTP/1.0\r\n", raw);
    }
}
```

### After (plain unit test — no ActorSystem)

```csharp
public sealed class Http10EncoderStageConversionExampleTests  // No base class
{
    private static string Encode(HttpRequestMessage request, int bufferSize = 8192)
    {
        var buffer = new Memory<byte>(new byte[bufferSize]);
        var written = Http10Encoder.Encode(request, ref buffer);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }

    [Fact]
    public void ST_10_ENC_001_Plain_RequestLine_Format()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/index.html")
        {
            Version = new Version(1, 0)
        };
        var raw = Encode(request);
        Assert.StartsWith("GET /index.html HTTP/1.0\r\n", raw);
    }
}
```

### Timing comparison

| Test | ActorSystem version | Plain version | Speedup |
|------|---------------------|---------------|---------|
| `ST_10_ENC_001_RequestLine_Format` | 342 ms | < 1 ms | **>300×** |
| `ST_10_ENC_002_CustomHeader_Forwarded` | 13 ms* | < 1 ms | ~13× |
| `ST_10_ENC_005_PostBody_FollowsHeaders` | 6 ms* | 3 ms | ~2× |

*\* Subsequent tests in the same class share the ActorSystem, so only the first test pays the full 342 ms startup cost. Across test classes, each class pays ~300 ms.*

### Concrete conversion example

File: `src/TurboHttp.Tests/RFC1945/18_EncoderStageConversionExampleTests.cs`

This file contains 4 tests converted from `Http10EncoderStageTests` to plain `[Fact]`
methods. All 4 produce the same assertion results as the originals and run without
an ActorSystem. See the file for the full implementation.

---

## Decision Guide

**Convert when:**
- Test calls `Source.Single(request).Via(EncoderStage).RunWith(Sink, Materializer)` — this is just `Encoder.Encode()` with extra steps
- Test calls `Source.From(chunks).Via(DecoderStage).RunWith(Sink, Materializer)` — this is just `Decoder.TryDecode()` with extra steps
- Test does not verify stream completion, timing, backpressure, or multi-element ordering

**Keep as stream test when:**
- Test uses `EngineFakeConnectionStage` or `H2EngineFakeConnectionStage` — tests full pipeline wiring
- Test uses `GraphDsl.Create` with multiple inlets/outlets — tests GraphStage behaviour
- Test verifies FIFO ordering across multiple pipelined requests
- Test uses `InitialDelay`, `Never`, or other timing-sensitive sources
- Test verifies `StreamAcquireItem` or `IControlItem` emission
