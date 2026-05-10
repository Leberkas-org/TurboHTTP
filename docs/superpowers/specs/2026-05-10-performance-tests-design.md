# Micro Benchmarks Design

## Goal

Create a dedicated `TurboHTTP.MicroBenchmarks` project using BenchmarkDotNet that establishes component-level performance baselines, detects regressions via JSON baseline comparison, and provides precise numbers to guide micro-optimization work.

## Scope

- **Protocol codecs**: HTTP/1.0, HTTP/1.1, HTTP/2 encoders and decoders
- **HPACK**: encoder, decoder, Huffman codec
- **Stream pipeline**: Engine flow composition, feedback buffer, version dispatch
- **Transport**: TCP loopback connection establishment latency
- **Not included**: HTTP/3 QUIC (experimental), end-to-end external server benchmarks (covered by existing `TurboHTTP.Benchmarks`)

## Metrics

Every benchmark uses `[MemoryDiagnoser]` and tracks:
- **Throughput**: ops/sec (via BenchmarkDotNet default)
- **Allocations**: bytes allocated per operation
- **Statistical columns**: Median + P95

## Project Structure

```
src/TurboHTTP.MicroBenchmarks/
├── Http10/
│   ├── Http10DecoderBenchmark.cs
│   └── Http10EncoderBenchmark.cs
├── Http11/
│   ├── Http11DecoderBenchmark.cs
│   ├── Http11EncoderBenchmark.cs
│   └── Http11ChunkedDecoderBenchmark.cs
├── Http2/
│   ├── Http2FrameDecoderBenchmark.cs
│   ├── Http2FrameEncoderBenchmark.cs
│   └── Http2ResponseDecoderBenchmark.cs
├── Hpack/
│   ├── HpackEncoderBenchmark.cs
│   ├── HpackDecoderBenchmark.cs
│   └── HuffmanBenchmark.cs
├── Pipeline/
│   ├── EngineFlowBenchmark.cs
│   ├── FeedbackBufferBenchmark.cs
│   └── VersionDispatchBenchmark.cs
├── Transport/
│   └── ConnectionSetupBenchmark.cs
├── Internal/
│   ├── BaselineComparer.cs
│   └── MicroBenchmarkConfig.cs
├── Baselines/
│   └── *.json
├── Program.cs
└── TurboHTTP.MicroBenchmarks.csproj
```

## Benchmark Pattern

```csharp
[MemoryDiagnoser]
[Config(typeof(MicroBenchmarkConfig))]
public class HpackDecoderBenchmark
{
    private byte[] _encodedHeaders = null!;
    private HpackDecoder _decoder = null!;

    [GlobalSetup]
    public void Setup()
    {
        _encodedHeaders = HpackTestData.TypicalResponseHeaders();
        _decoder = new HpackDecoder(4096);
    }

    [Benchmark(Baseline = true)]
    public int DecodeTypicalHeaders()
    {
        _decoder.Reset();
        return _decoder.Decode(_encodedHeaders);
    }
}
```

Key rules:
- One `[Benchmark(Baseline = true)]` per class for the typical case
- `[Params]` for size/count variations where relevant
- Pre-allocated inputs in `[GlobalSetup]`
- Return values to prevent dead-code elimination
- No `async` benchmarks for codec-level tests (they are synchronous)

## Shared Config

```csharp
public class MicroBenchmarkConfig : ManualConfig
{
    public MicroBenchmarkConfig()
    {
        AddDiagnoser(MemoryDiagnoser.Default);
        AddExporter(JsonExporter.Full);
        AddColumn(StatisticColumn.Median);
        AddColumn(StatisticColumn.P95);
        WithOptions(ConfigOptions.DisableOptimizationsValidator);
    }
}
```

## Baseline Comparison

`BaselineComparer` reads committed JSON exports and compares against current results:

- **Throughput regression threshold**: warn if > 10% slower than baseline
- **Allocation regression threshold**: warn if > 5% more allocations than baseline
- Output: markdown comparison table to console
- Exit code: always 0 (informational, not a gate)

### Workflow

1. **Run benchmarks**: `dotnet run -c Release --project TurboHTTP.MicroBenchmarks -- --filter *Hpack* --exporters json`
2. **First run** (no baseline): results saved to `Baselines/` as the new baseline
3. **Subsequent runs**: compare against baseline, print regression report
4. **Update baseline**: `--update-baseline` flag overwrites the committed JSON

## Pipeline Benchmarks — Special Handling

Pipeline benchmarks (`Pipeline/` folder) require an Akka `ActorSystem` and `IMaterializer`:

- Shared `ActorSystem` created in `[GlobalSetup]`, terminated in `[GlobalCleanup]`
- Fake transport flows (same pattern as `LoopbackBenchmarkStageSpec`) isolate pipeline overhead from network I/O
- `Engine.CreateFlow` composition measured separately from per-request throughput

## Transport Benchmarks

- Loopback TCP only — measures connection establishment latency (handshake overhead)
- Does NOT measure sustained throughput (covered by existing `TurboHTTP.Benchmarks`)

## Component Coverage Matrix

| Component | Throughput | Allocations | Params |
|-----------|-----------|-------------|--------|
| HTTP/1.0 Decoder | ops/sec | bytes/op | payload size |
| HTTP/1.0 Encoder | ops/sec | bytes/op | header count |
| HTTP/1.1 Decoder | ops/sec | bytes/op | payload size, chunked vs content-length |
| HTTP/1.1 Encoder | ops/sec | bytes/op | header count |
| HTTP/1.1 Chunked Decoder | ops/sec | bytes/op | chunk count, chunk size |
| HTTP/2 Frame Decoder | ops/sec | bytes/op | frame type, payload size |
| HTTP/2 Frame Encoder | ops/sec | bytes/op | frame type |
| HTTP/2 Response Decoder | ops/sec | bytes/op | header count, body size |
| HPACK Encoder | ops/sec | bytes/op | header count, table size |
| HPACK Decoder | ops/sec | bytes/op | header count, table size |
| Huffman | ops/sec | bytes/op | input length |
| Engine Flow | ops/sec | bytes/op | — |
| Feedback Buffer | ops/sec | bytes/op | redirect count |
| Version Dispatch | ops/sec | bytes/op | — |
| TCP Connection Setup | latency (ms) | bytes/op | — |

## Dependencies

- `BenchmarkDotNet` (latest stable)
- `TurboHTTP` (project reference)
- `TurboHTTP.Tests.Shared` (for fake transport helpers, if extracted)
