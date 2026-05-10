# Micro Benchmarks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create `TurboHTTP.MicroBenchmarks` — a BenchmarkDotNet project that establishes component-level baselines, detects regressions via JSON comparison, and guides micro-optimization work across protocol codecs, HPACK, pipeline, and transport layers.

**Architecture:** Dedicated console project using BenchmarkDotNet with `[MemoryDiagnoser]` and JSON export. Each benchmark class targets one component. A `BaselineComparer` reads committed JSON baselines and prints a markdown regression report (10% throughput / 5% allocation thresholds). All protocol types are `internal` — the project gets access via `InternalsVisibleTo`.

**Tech Stack:** BenchmarkDotNet 0.15.8 (already in `Directory.Packages.props`), .NET 10, Akka.Streams (for pipeline benchmarks), `Servus.Akka.Transport.TransportBuffer`

---

## File Map

| File | Responsibility |
|------|----------------|
| `src/TurboHTTP.MicroBenchmarks/TurboHTTP.MicroBenchmarks.csproj` | Project file — Exe, references TurboHTTP + Tests.Shared, BenchmarkDotNet |
| `src/TurboHTTP.MicroBenchmarks/Program.cs` | BenchmarkSwitcher entry point + baseline comparison post-run |
| `src/TurboHTTP.MicroBenchmarks/Internal/MicroBenchmarkConfig.cs` | Shared ManualConfig: MemoryDiagnoser, JsonExporter, Median/P95 columns |
| `src/TurboHTTP.MicroBenchmarks/Internal/BaselineComparer.cs` | Loads JSON baselines, compares against current results, prints markdown report |
| `src/TurboHTTP.MicroBenchmarks/Http10/Http10DecoderBenchmark.cs` | HTTP/1.0 response parsing throughput + allocations |
| `src/TurboHTTP.MicroBenchmarks/Http10/Http10EncoderBenchmark.cs` | HTTP/1.0 request encoding throughput + allocations |
| `src/TurboHTTP.MicroBenchmarks/Http11/Http11DecoderBenchmark.cs` | HTTP/1.1 response parsing (content-length) throughput + allocations |
| `src/TurboHTTP.MicroBenchmarks/Http11/Http11EncoderBenchmark.cs` | HTTP/1.1 request encoding throughput + allocations |
| `src/TurboHTTP.MicroBenchmarks/Http11/Http11ChunkedDecoderBenchmark.cs` | HTTP/1.1 chunked transfer decoding in isolation |
| `src/TurboHTTP.MicroBenchmarks/Http2/Http2FrameDecoderBenchmark.cs` | HTTP/2 frame parsing throughput + allocations |
| `src/TurboHTTP.MicroBenchmarks/Http2/Http2FrameEncoderBenchmark.cs` | HTTP/2 frame building (RequestEncoder) throughput + allocations |
| `src/TurboHTTP.MicroBenchmarks/Http2/Http2ResponseDecoderBenchmark.cs` | HTTP/2 full response assembly throughput + allocations |
| `src/TurboHTTP.MicroBenchmarks/Hpack/HpackEncoderBenchmark.cs` | HPACK header compression throughput + allocations |
| `src/TurboHTTP.MicroBenchmarks/Hpack/HpackDecoderBenchmark.cs` | HPACK header decompression throughput + allocations |
| `src/TurboHTTP.MicroBenchmarks/Hpack/HuffmanBenchmark.cs` | Huffman encode/decode in isolation |
| `src/TurboHTTP.MicroBenchmarks/Pipeline/EngineFlowBenchmark.cs` | Engine.CreateFlow + single request roundtrip (Akka, fake transport) |
| `src/TurboHTTP.MicroBenchmarks/Pipeline/FeedbackBufferBenchmark.cs` | Redirect/retry feedback loop overhead (Akka, fake transport) |
| `src/TurboHTTP.MicroBenchmarks/Pipeline/VersionDispatchBenchmark.cs` | Version routing overhead (Akka, fake transport) |
| `src/TurboHTTP.MicroBenchmarks/Transport/ConnectionSetupBenchmark.cs` | TCP loopback connection establishment latency |
| `src/TurboHTTP/TurboHTTP.csproj` | Add `InternalsVisibleTo` for `TurboHTTP.MicroBenchmarks` |
| `src/TurboHTTP.Tests.Shared/TurboHTTP.Tests.Shared.csproj` | Add `InternalsVisibleTo` for `TurboHTTP.MicroBenchmarks` |
| `src/TurboHTTP.slnx` | Add project entry |

---

### Task 1: Project Scaffolding

**Files:**
- Create: `src/TurboHTTP.MicroBenchmarks/TurboHTTP.MicroBenchmarks.csproj`
- Modify: `src/TurboHTTP/TurboHTTP.csproj:38-47` (add InternalsVisibleTo)
- Modify: `src/TurboHTTP.Tests.Shared/TurboHTTP.Tests.Shared.csproj:19-23` (add InternalsVisibleTo)
- Modify: `src/TurboHTTP.slnx` (add project entry)

- [ ] **Step 1: Create the project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
        <Optimize>true</Optimize>
        <IsPackable>false</IsPackable>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="BenchmarkDotNet"/>
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\TurboHTTP\TurboHTTP.csproj"/>
        <ProjectReference Include="..\TurboHTTP.Tests.Shared\TurboHTTP.Tests.Shared.csproj"/>
    </ItemGroup>

</Project>
```

- [ ] **Step 2: Add InternalsVisibleTo in TurboHTTP.csproj**

Add after the existing `TurboHTTP.Benchmarks` entry (line 45):

```xml
<InternalsVisibleTo Include="TurboHTTP.MicroBenchmarks"/>
```

- [ ] **Step 3: Add InternalsVisibleTo in TurboHTTP.Tests.Shared.csproj**

Add after the existing `TurboHTTP.AcceptanceTests` entry (line 22):

```xml
<InternalsVisibleTo Include="TurboHTTP.MicroBenchmarks"/>
```

- [ ] **Step 4: Add project to TurboHTTP.slnx**

Add after the `TurboHTTP.Benchmarks` project entry:

```xml
<Project Path="TurboHTTP.MicroBenchmarks/TurboHTTP.MicroBenchmarks.csproj" />
```

- [ ] **Step 5: Verify build**

Run: `dotnet build --configuration Release src/TurboHTTP.MicroBenchmarks/TurboHTTP.MicroBenchmarks.csproj`
Expected: Build succeeded (no source files yet, just project references)

- [ ] **Step 6: Commit**

```bash
git add src/TurboHTTP.MicroBenchmarks/TurboHTTP.MicroBenchmarks.csproj src/TurboHTTP/TurboHTTP.csproj src/TurboHTTP.Tests.Shared/TurboHTTP.Tests.Shared.csproj src/TurboHTTP.slnx
git commit -m "feat: scaffold TurboHTTP.MicroBenchmarks project"
```

---

### Task 2: Shared Config + Program.cs

**Files:**
- Create: `src/TurboHTTP.MicroBenchmarks/Internal/MicroBenchmarkConfig.cs`
- Create: `src/TurboHTTP.MicroBenchmarks/Program.cs`

- [ ] **Step 1: Create MicroBenchmarkConfig**

```csharp
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;

namespace TurboHTTP.MicroBenchmarks.Internal;

public sealed class MicroBenchmarkConfig : ManualConfig
{
    public MicroBenchmarkConfig()
    {
        AddJob(Job.Default.WithGcServer(true));
        AddDiagnoser(MemoryDiagnoser.Default);
        AddExporter(JsonExporter.Full);
        AddColumn(StatisticColumn.Median);
        AddColumn(StatisticColumn.P95);
    }
}
```

- [ ] **Step 2: Create Program.cs**

```csharp
using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
```

- [ ] **Step 3: Verify build**

Run: `dotnet build --configuration Release src/TurboHTTP.MicroBenchmarks/TurboHTTP.MicroBenchmarks.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/TurboHTTP.MicroBenchmarks/Internal/MicroBenchmarkConfig.cs src/TurboHTTP.MicroBenchmarks/Program.cs
git commit -m "feat: add shared benchmark config and entry point"
```

---

### Task 3: BaselineComparer

**Files:**
- Create: `src/TurboHTTP.MicroBenchmarks/Internal/BaselineComparer.cs`

- [ ] **Step 1: Create BaselineComparer**

This class reads BenchmarkDotNet JSON export files and compares current results against committed baselines.

```csharp
using System.Text;
using System.Text.Json;

namespace TurboHTTP.MicroBenchmarks.Internal;

public sealed record BaselineEntry(
    string Method,
    double MedianNanoseconds,
    long AllocatedBytes);

public static class BaselineComparer
{
    private const double ThroughputRegressionThreshold = 0.10;
    private const double AllocationRegressionThreshold = 0.05;

    public static IReadOnlyList<BaselineEntry> LoadBaseline(string jsonPath)
    {
        if (!File.Exists(jsonPath))
        {
            return [];
        }

        using var stream = File.OpenRead(jsonPath);
        using var doc = JsonDocument.Parse(stream);

        var entries = new List<BaselineEntry>();
        var benchmarks = doc.RootElement.GetProperty("Benchmarks");

        foreach (var bm in benchmarks.EnumerateArray())
        {
            var method = bm.GetProperty("FullName").GetString() ?? "";
            var stats = bm.GetProperty("Statistics");
            var median = stats.GetProperty("Median").GetDouble();

            long allocated = 0;
            if (bm.TryGetProperty("Memory", out var mem)
                && mem.TryGetProperty("BytesAllocatedPerOperation", out var allocProp))
            {
                allocated = allocProp.GetInt64();
            }

            entries.Add(new BaselineEntry(method, median, allocated));
        }

        return entries;
    }

    public static string Compare(
        IReadOnlyList<BaselineEntry> baseline,
        IReadOnlyList<BaselineEntry> current)
    {
        if (baseline.Count == 0)
        {
            return "No baseline found — current results will become the new baseline.";
        }

        var baselineMap = baseline.ToDictionary(e => e.Method, StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.AppendLine("# Performance Regression Report");
        sb.AppendLine();
        sb.AppendLine("| Benchmark | Baseline Median (ns) | Current Median (ns) | Δ% | Baseline Alloc (B) | Current Alloc (B) | Δ% | Status |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---|");

        var regressions = 0;

        foreach (var entry in current)
        {
            if (!baselineMap.TryGetValue(entry.Method, out var b))
            {
                sb.AppendLine($"| {entry.Method} | — | {entry.MedianNanoseconds:N0} | NEW | — | {entry.AllocatedBytes:N0} | NEW | ℹ️ |");
                continue;
            }

            var throughputDelta = b.MedianNanoseconds > 0
                ? (entry.MedianNanoseconds - b.MedianNanoseconds) / b.MedianNanoseconds
                : 0;

            var allocDelta = b.AllocatedBytes > 0
                ? (double)(entry.AllocatedBytes - b.AllocatedBytes) / b.AllocatedBytes
                : 0;

            var throughputRegression = throughputDelta > ThroughputRegressionThreshold;
            var allocRegression = allocDelta > AllocationRegressionThreshold;
            var status = throughputRegression || allocRegression ? "REGRESSION" : "OK";

            if (throughputRegression || allocRegression)
            {
                regressions++;
            }

            sb.AppendLine(string.Concat(
                $"| {entry.Method} ",
                $"| {b.MedianNanoseconds:N0} ",
                $"| {entry.MedianNanoseconds:N0} ",
                $"| {throughputDelta:+0.0%;-0.0%;0.0%} ",
                $"| {b.AllocatedBytes:N0} ",
                $"| {entry.AllocatedBytes:N0} ",
                $"| {allocDelta:+0.0%;-0.0%;0.0%} ",
                $"| {status} |"));
        }

        sb.AppendLine();
        sb.AppendLine(regressions > 0
            ? $"**{regressions} regression(s) detected.**"
            : "**No regressions detected.**");

        return sb.ToString();
    }

    public static void SaveBaseline(string sourcePath, string baselineDir)
    {
        Directory.CreateDirectory(baselineDir);
        var fileName = Path.GetFileName(sourcePath);
        File.Copy(sourcePath, Path.Combine(baselineDir, fileName), overwrite: true);
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build --configuration Release src/TurboHTTP.MicroBenchmarks/TurboHTTP.MicroBenchmarks.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/TurboHTTP.MicroBenchmarks/Internal/BaselineComparer.cs
git commit -m "feat: add baseline comparison for regression detection"
```

---

### Task 4: HTTP/1.0 Benchmarks

**Files:**
- Create: `src/TurboHTTP.MicroBenchmarks/Http10/Http10DecoderBenchmark.cs`
- Create: `src/TurboHTTP.MicroBenchmarks/Http10/Http10EncoderBenchmark.cs`

- [ ] **Step 1: Create Http10DecoderBenchmark**

The `Http10.Decoder` is `internal sealed class` in namespace `TurboHTTP.Protocol.Http10`. It has:
- Constructor: `Decoder(int maxHeaderSize = 16384, int maxTotalHeaderSize = 65536)`
- Method: `bool TryDecode(ReadOnlyMemory<byte> incomingData, out HttpResponseMessage? response)`
- Method: `bool TryDecodeEof(out HttpResponseMessage? response)`
- Method: `void Reset()` — for reuse between iterations

```csharp
using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Http10;

namespace TurboHTTP.MicroBenchmarks.Http10;

[Config(typeof(MicroBenchmarkConfig))]
public class Http10DecoderBenchmark
{
    private byte[] _smallResponse = null!;
    private byte[] _largeResponse = null!;
    private Decoder _decoder = null!;

    [GlobalSetup]
    public void Setup()
    {
        _decoder = new Decoder();

        _smallResponse = "HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nHello"u8.ToArray();

        var largeBody = new byte[8192];
        Array.Fill(largeBody, (byte)'X');
        _largeResponse = System.Text.Encoding.Latin1.GetBytes(
            string.Concat("HTTP/1.0 200 OK\r\nContent-Length: 8192\r\n\r\n",
                new string('X', 8192)));
    }

    [Benchmark(Baseline = true)]
    public bool DecodeSmallResponse()
    {
        _decoder.Reset();
        return _decoder.TryDecode(_smallResponse, out _);
    }

    [Benchmark]
    public bool DecodeLargeResponse()
    {
        _decoder.Reset();
        return _decoder.TryDecode(_largeResponse, out _);
    }
}
```

- [ ] **Step 2: Create Http10EncoderBenchmark**

The `Http10.Encoder` is `internal static class` in namespace `TurboHTTP.Protocol.Http10`. It has:
- Method: `static int Encode(HttpRequestMessage request, ref Span<byte> buffer, bool absoluteForm = false)`
- Returns: total bytes written to buffer

```csharp
using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Http10;

namespace TurboHTTP.MicroBenchmarks.Http10;

[Config(typeof(MicroBenchmarkConfig))]
public class Http10EncoderBenchmark
{
    private HttpRequestMessage _simpleGet = null!;
    private HttpRequestMessage _requestWithHeaders = null!;
    private byte[] _buffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _buffer = new byte[16384];

        _simpleGet = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");

        _requestWithHeaders = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api/data");
        _requestWithHeaders.Headers.TryAddWithoutValidation("Accept", "application/json");
        _requestWithHeaders.Headers.TryAddWithoutValidation("Authorization", "Bearer token123");
        _requestWithHeaders.Headers.TryAddWithoutValidation("X-Request-Id", "bench-001");
        _requestWithHeaders.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        _requestWithHeaders.Content = new ByteArrayContent(new byte[256]);
    }

    [Benchmark(Baseline = true)]
    public int EncodeSimpleGet()
    {
        var span = _buffer.AsSpan();
        return Encoder.Encode(_simpleGet, ref span);
    }

    [Benchmark]
    public int EncodeWithHeaders()
    {
        var span = _buffer.AsSpan();
        return Encoder.Encode(_requestWithHeaders, ref span);
    }
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build --configuration Release src/TurboHTTP.MicroBenchmarks/TurboHTTP.MicroBenchmarks.csproj`
Expected: Build succeeded

- [ ] **Step 4: Dry-run to validate benchmark discovery**

Run: `dotnet run --configuration Release --project src/TurboHTTP.MicroBenchmarks/TurboHTTP.MicroBenchmarks.csproj -- --filter *Http10* --job Dry`
Expected: Both benchmark classes discovered and execute (Dry mode = 1 iteration, fast)

- [ ] **Step 5: Commit**

```bash
git add src/TurboHTTP.MicroBenchmarks/Http10/
git commit -m "feat: add HTTP/1.0 encoder and decoder benchmarks"
```

---

### Task 5: HTTP/1.1 Benchmarks

**Files:**
- Create: `src/TurboHTTP.MicroBenchmarks/Http11/Http11DecoderBenchmark.cs`
- Create: `src/TurboHTTP.MicroBenchmarks/Http11/Http11EncoderBenchmark.cs`
- Create: `src/TurboHTTP.MicroBenchmarks/Http11/Http11ChunkedDecoderBenchmark.cs`

- [ ] **Step 1: Create Http11DecoderBenchmark**

The `Http11.Decoder` is `internal sealed class : IDisposable` in namespace `TurboHTTP.Protocol.Http11`. It has:
- Constructor: `Decoder(int maxHeaderSize = 16384, int maxTotalHeaderSize = 65536, int maxBodySize = 10485760, int maxHeaderCount = 100)`
- Method: `bool TryDecode(ReadOnlyMemory<byte> incomingData, out IReadOnlyList<HttpResponseMessage> responses)`
- Method: `void Reset()` — for reuse
- Method: `void Dispose()` — MemoryPool cleanup

```csharp
using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.MicroBenchmarks.Http11;

[Config(typeof(MicroBenchmarkConfig))]
public class Http11DecoderBenchmark
{
    private byte[] _smallResponse = null!;
    private byte[] _largeResponse = null!;
    private byte[] _multipleHeaders = null!;
    private Decoder _decoder = null!;

    [GlobalSetup]
    public void Setup()
    {
        _decoder = new Decoder();

        _smallResponse = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: keep-alive\r\n\r\nHello"u8.ToArray();

        _largeResponse = System.Text.Encoding.Latin1.GetBytes(
            string.Concat("HTTP/1.1 200 OK\r\nContent-Length: 8192\r\nConnection: keep-alive\r\n\r\n",
                new string('X', 8192)));

        var headers = new System.Text.StringBuilder();
        headers.Append("HTTP/1.1 200 OK\r\n");
        for (var i = 0; i < 50; i++)
        {
            headers.Append($"X-Header-{i}: value-{i}\r\n");
        }
        headers.Append("Content-Length: 2\r\n\r\nOK");
        _multipleHeaders = System.Text.Encoding.Latin1.GetBytes(headers.ToString());
    }

    [GlobalCleanup]
    public void Cleanup() => _decoder.Dispose();

    [Benchmark(Baseline = true)]
    public bool DecodeSmallResponse()
    {
        _decoder.Reset();
        return _decoder.TryDecode(_smallResponse, out _);
    }

    [Benchmark]
    public bool DecodeLargeResponse()
    {
        _decoder.Reset();
        return _decoder.TryDecode(_largeResponse, out _);
    }

    [Benchmark]
    public bool Decode50Headers()
    {
        _decoder.Reset();
        return _decoder.TryDecode(_multipleHeaders, out _);
    }
}
```

- [ ] **Step 2: Create Http11EncoderBenchmark**

The `Http11.Encoder` is `internal static class` in namespace `TurboHTTP.Protocol.Http11`. It has:
- Method: `static int Encode(HttpRequestMessage request, ref Span<byte> buffer, bool absoluteForm = false)`

```csharp
using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.MicroBenchmarks.Http11;

[Config(typeof(MicroBenchmarkConfig))]
public class Http11EncoderBenchmark
{
    private HttpRequestMessage _simpleGet = null!;
    private HttpRequestMessage _postWithBody = null!;
    private byte[] _buffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _buffer = new byte[16384];

        _simpleGet = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path")
        {
            Version = new Version(1, 1)
        };

        _postWithBody = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api/data")
        {
            Version = new Version(1, 1)
        };
        _postWithBody.Headers.TryAddWithoutValidation("Accept", "application/json");
        _postWithBody.Headers.TryAddWithoutValidation("Authorization", "Bearer token123456789");
        _postWithBody.Headers.TryAddWithoutValidation("X-Request-Id", "perf-bench-001");
        _postWithBody.Content = new ByteArrayContent(new byte[1024]);
        _postWithBody.Content.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");
    }

    [Benchmark(Baseline = true)]
    public int EncodeSimpleGet()
    {
        var span = _buffer.AsSpan();
        return Encoder.Encode(_simpleGet, ref span);
    }

    [Benchmark]
    public int EncodePostWithBody()
    {
        var span = _buffer.AsSpan();
        return Encoder.Encode(_postWithBody, ref span);
    }
}
```

- [ ] **Step 3: Create Http11ChunkedDecoderBenchmark**

Chunked decoding is handled internally by the same `Http11.Decoder`. We just feed it chunked responses.

```csharp
using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.MicroBenchmarks.Http11;

[Config(typeof(MicroBenchmarkConfig))]
public class Http11ChunkedDecoderBenchmark
{
    private byte[] _singleChunk = null!;
    private byte[] _manySmallChunks = null!;
    private Decoder _decoder = null!;

    [GlobalSetup]
    public void Setup()
    {
        _decoder = new Decoder();

        _singleChunk = System.Text.Encoding.Latin1.GetBytes(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "100\r\n" + new string('A', 256) + "\r\n0\r\n\r\n");

        var sb = new System.Text.StringBuilder();
        sb.Append("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n");
        for (var i = 0; i < 20; i++)
        {
            sb.Append("10\r\n");
            sb.Append(new string('B', 16));
            sb.Append("\r\n");
        }
        sb.Append("0\r\n\r\n");
        _manySmallChunks = System.Text.Encoding.Latin1.GetBytes(sb.ToString());
    }

    [GlobalCleanup]
    public void Cleanup() => _decoder.Dispose();

    [Benchmark(Baseline = true)]
    public bool DecodeSingleChunk()
    {
        _decoder.Reset();
        return _decoder.TryDecode(_singleChunk, out _);
    }

    [Benchmark]
    public bool Decode20SmallChunks()
    {
        _decoder.Reset();
        return _decoder.TryDecode(_manySmallChunks, out _);
    }
}
```

- [ ] **Step 4: Verify build**

Run: `dotnet build --configuration Release src/TurboHTTP.MicroBenchmarks/TurboHTTP.MicroBenchmarks.csproj`
Expected: Build succeeded

- [ ] **Step 5: Dry-run**

Run: `dotnet run --configuration Release --project src/TurboHTTP.MicroBenchmarks/TurboHTTP.MicroBenchmarks.csproj -- --filter *Http11* --job Dry`
Expected: All 3 classes discovered and pass

- [ ] **Step 6: Commit**

```bash
git add src/TurboHTTP.MicroBenchmarks/Http11/
git commit -m "feat: add HTTP/1.1 encoder, decoder, and chunked benchmarks"
```

---

### Task 6: HTTP/2 Frame Benchmarks

**Files:**
- Create: `src/TurboHTTP.MicroBenchmarks/Http2/Http2FrameDecoderBenchmark.cs`
- Create: `src/TurboHTTP.MicroBenchmarks/Http2/Http2FrameEncoderBenchmark.cs`
- Create: `src/TurboHTTP.MicroBenchmarks/Http2/Http2ResponseDecoderBenchmark.cs`

- [ ] **Step 1: Create Http2FrameDecoderBenchmark**

The `Http2.FrameDecoder` is `internal sealed class : IDisposable`. It has:
- Method: `IReadOnlyList<Http2Frame> Decode(TransportBuffer buffer)` — takes ownership of buffer
- Method: `void Reset()`, `void Dispose()`
- `TransportBuffer` has implicit conversion from `byte[]` (rents from pool, copies data)

Since `Decode()` takes ownership of the `TransportBuffer`, we must create a new one per iteration.

```csharp
using BenchmarkDotNet.Attributes;
using Servus.Akka.Transport;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.MicroBenchmarks.Http2;

[Config(typeof(MicroBenchmarkConfig))]
public class Http2FrameDecoderBenchmark
{
    private byte[] _settingsFrame = null!;
    private byte[] _dataFrame = null!;
    private byte[] _multipleFrames = null!;
    private FrameDecoder _decoder = null!;

    [GlobalSetup]
    public void Setup()
    {
        _decoder = new FrameDecoder();

        // SETTINGS frame: stream 0, no flags, 1 entry (INITIAL_WINDOW_SIZE = 65535)
        _settingsFrame =
        [
            0x00, 0x00, 0x06, // length = 6
            0x04,             // type = SETTINGS
            0x00,             // flags = 0
            0x00, 0x00, 0x00, 0x00, // stream ID = 0
            0x00, 0x04,       // INITIAL_WINDOW_SIZE
            0x00, 0x00, 0xFF, 0xFF  // value = 65535
        ];

        // DATA frame: stream 1, END_STREAM, 128 bytes payload
        var payload = new byte[128];
        Array.Fill(payload, (byte)'D');
        _dataFrame = new byte[9 + payload.Length];
        _dataFrame[0] = 0x00;
        _dataFrame[1] = 0x00;
        _dataFrame[2] = (byte)payload.Length;
        _dataFrame[3] = 0x00; // DATA
        _dataFrame[4] = 0x01; // END_STREAM
        _dataFrame[5] = 0x00;
        _dataFrame[6] = 0x00;
        _dataFrame[7] = 0x00;
        _dataFrame[8] = 0x01; // stream 1
        Array.Copy(payload, 0, _dataFrame, 9, payload.Length);

        // 10 SETTINGS ACK frames
        var ms = new MemoryStream();
        for (var i = 0; i < 10; i++)
        {
            ms.Write(new byte[] { 0x00, 0x00, 0x00, 0x04, 0x01, 0x00, 0x00, 0x00, 0x00 });
        }
        _multipleFrames = ms.ToArray();
    }

    [GlobalCleanup]
    public void Cleanup() => _decoder.Dispose();

    [Benchmark(Baseline = true)]
    public int DecodeSettingsFrame()
    {
        _decoder.Reset();
        TransportBuffer buf = _settingsFrame;
        var frames = _decoder.Decode(buf);
        return frames.Count;
    }

    [Benchmark]
    public int DecodeDataFrame()
    {
        _decoder.Reset();
        TransportBuffer buf = _dataFrame;
        var frames = _decoder.Decode(buf);
        return frames.Count;
    }

    [Benchmark]
    public int Decode10SettingsAck()
    {
        _decoder.Reset();
        TransportBuffer buf = _multipleFrames;
        var frames = _decoder.Decode(buf);
        return frames.Count;
    }
}
```

- [ ] **Step 2: Create Http2FrameEncoderBenchmark**

The `Http2.RequestEncoder` is `internal sealed class`. It has:
- Constructor: `RequestEncoder(bool useHuffman = false, int maxFrameSize = 16384)`
- Method: `IReadOnlyList<Http2Frame> Encode(HttpRequestMessage request, int streamId)`

```csharp
using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.MicroBenchmarks.Http2;

[Config(typeof(MicroBenchmarkConfig))]
public class Http2FrameEncoderBenchmark
{
    private RequestEncoder _encoder = null!;
    private HttpRequestMessage _simpleGet = null!;
    private HttpRequestMessage _postWithBody = null!;
    private int _streamId;

    [GlobalSetup]
    public void Setup()
    {
        _encoder = new RequestEncoder(useHuffman: true);

        _simpleGet = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path")
        {
            Version = new Version(2, 0)
        };

        _postWithBody = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api/data")
        {
            Version = new Version(2, 0)
        };
        _postWithBody.Headers.TryAddWithoutValidation("Accept", "application/json");
        _postWithBody.Headers.TryAddWithoutValidation("Authorization", "Bearer token123456789");
        _postWithBody.Content = new ByteArrayContent(new byte[1024]);
    }

    [Benchmark(Baseline = true)]
    public int EncodeSimpleGet()
    {
        _streamId += 2;
        var frames = _encoder.Encode(_simpleGet, _streamId);
        return frames.Count;
    }

    [Benchmark]
    public int EncodePostWithBody()
    {
        _streamId += 2;
        var frames = _encoder.Encode(_postWithBody, _streamId);
        return frames.Count;
    }
}
```

- [ ] **Step 3: Create Http2ResponseDecoderBenchmark**

The `Http2.ResponseDecoder` is `internal sealed class`. It has:
- Constructor: `ResponseDecoder(HpackDecoder hpack, int maxHeaderSize = 16384, int maxTotalHeaderSize = 65536)`
- Method: `HttpResponseMessage? DecodeHeaders(int streamId, bool endStream, StreamState state)`
- It works with `StreamState` which tracks HPACK-decoded headers per stream.

This benchmark measures decoding a complete HEADERS frame into an `HttpResponseMessage`. We must first HPACK-decode the header block, set it on a `StreamState`, then call `DecodeHeaders`.

```csharp
using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.MicroBenchmarks.Http2;

[Config(typeof(MicroBenchmarkConfig))]
public class Http2ResponseDecoderBenchmark
{
    private HpackDecoder _hpackDecoder = null!;
    private ResponseDecoder _responseDecoder = null!;
    private byte[] _encodedHeaders = null!;

    [GlobalSetup]
    public void Setup()
    {
        _hpackDecoder = new HpackDecoder();
        _responseDecoder = new ResponseDecoder(_hpackDecoder);

        var encoder = new HpackEncoder(useHuffman: true);
        var headers = new List<HpackHeader>
        {
            new(":status", "200"),
            new("content-type", "application/json"),
            new("content-length", "1024"),
            new("server", "TurboBench"),
            new("date", "Sat, 10 May 2026 12:00:00 GMT")
        };

        var output = new byte[4096];
        var span = output.AsSpan();
        var written = encoder.Encode(headers, ref span);
        _encodedHeaders = output[..written];
    }

    [Benchmark(Baseline = true)]
    public HttpResponseMessage? DecodeTypicalResponse()
    {
        var decodedHeaders = _hpackDecoder.Decode(_encodedHeaders);
        var state = new StreamState(1);
        state.SetHeaders(decodedHeaders);
        return _responseDecoder.DecodeHeaders(1, endStream: true, state);
    }
}
```

**Note:** The `StreamState` constructor and `SetHeaders` method signatures need to be verified at implementation time. If `StreamState` is not directly constructable, the implementer should check `src/TurboHTTP/Protocol/Http2/StreamState.cs` and adapt accordingly — possibly using the `StreamTracker` to obtain a `StreamState` instance. The key constraint is that the benchmark must isolate ResponseDecoder work from FrameDecoder and transport overhead.

- [ ] **Step 4: Verify build**

Run: `dotnet build --configuration Release src/TurboHTTP.MicroBenchmarks/TurboHTTP.MicroBenchmarks.csproj`
Expected: Build succeeded

- [ ] **Step 5: Dry-run**

Run: `dotnet run --configuration Release --project src/TurboHTTP.MicroBenchmarks/TurboHTTP.MicroBenchmarks.csproj -- --filter *Http2* --job Dry`
Expected: All 3 classes discovered and pass

- [ ] **Step 6: Commit**

```bash
git add src/TurboHTTP.MicroBenchmarks/Http2/
git commit -m "feat: add HTTP/2 frame decoder, encoder, and response decoder benchmarks"
```

---

### Task 7: HPACK Benchmarks

**Files:**
- Create: `src/TurboHTTP.MicroBenchmarks/Hpack/HpackEncoderBenchmark.cs`
- Create: `src/TurboHTTP.MicroBenchmarks/Hpack/HpackDecoderBenchmark.cs`
- Create: `src/TurboHTTP.MicroBenchmarks/Hpack/HuffmanBenchmark.cs`

- [ ] **Step 1: Create HpackEncoderBenchmark**

The `HpackEncoder` is `internal sealed class` in namespace `TurboHTTP.Protocol.Http2.Hpack`. It has:
- Constructor: `HpackEncoder(bool useHuffman = true)`
- Method: `int Encode(IReadOnlyList<HpackHeader> headers, ref Span<byte> output, bool useHuffman = true)`
- `HpackHeader` is `internal readonly record struct HpackHeader(string Name, string Value, bool NeverIndex = false)`

```csharp
using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.MicroBenchmarks.Hpack;

[Config(typeof(MicroBenchmarkConfig))]
public class HpackEncoderBenchmark
{
    private HpackEncoder _encoder = null!;
    private List<HpackHeader> _typicalHeaders = null!;
    private List<HpackHeader> _largeHeaderSet = null!;
    private byte[] _outputBuffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _encoder = new HpackEncoder(useHuffman: true);
        _outputBuffer = new byte[65536];

        _typicalHeaders =
        [
            new(":method", "GET"),
            new(":scheme", "https"),
            new(":path", "/api/v1/users"),
            new(":authority", "example.com"),
            new("accept", "application/json"),
            new("accept-encoding", "gzip, deflate, br"),
            new("user-agent", "TurboHTTP/1.0"),
        ];

        _largeHeaderSet = [];
        _largeHeaderSet.Add(new(":method", "POST"));
        _largeHeaderSet.Add(new(":scheme", "https"));
        _largeHeaderSet.Add(new(":path", "/api/v1/data"));
        _largeHeaderSet.Add(new(":authority", "example.com"));
        for (var i = 0; i < 30; i++)
        {
            _largeHeaderSet.Add(new($"x-custom-header-{i}", $"value-{i}-with-some-content"));
        }
    }

    [Benchmark(Baseline = true)]
    public int EncodeTypicalHeaders()
    {
        var span = _outputBuffer.AsSpan();
        return _encoder.Encode(_typicalHeaders, ref span);
    }

    [Benchmark]
    public int Encode34Headers()
    {
        var span = _outputBuffer.AsSpan();
        return _encoder.Encode(_largeHeaderSet, ref span);
    }
}
```

- [ ] **Step 2: Create HpackDecoderBenchmark**

The `HpackDecoder` is `internal sealed class` in namespace `TurboHTTP.Protocol.Http2.Hpack`. It has:
- Constructor: default (no explicit params)
- Method: `List<HpackHeader> Decode(ReadOnlySpan<byte> data)` — returns reused list (caller must consume immediately)

```csharp
using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.MicroBenchmarks.Hpack;

[Config(typeof(MicroBenchmarkConfig))]
public class HpackDecoderBenchmark
{
    private HpackDecoder _decoder = null!;
    private byte[] _typicalEncoded = null!;
    private byte[] _largeEncoded = null!;

    [GlobalSetup]
    public void Setup()
    {
        _decoder = new HpackDecoder();

        var encoder = new HpackEncoder(useHuffman: true);

        var typicalHeaders = new List<HpackHeader>
        {
            new(":status", "200"),
            new("content-type", "application/json"),
            new("content-length", "1024"),
            new("server", "nginx"),
            new("date", "Sat, 10 May 2026 12:00:00 GMT"),
            new("cache-control", "max-age=3600"),
            new("vary", "Accept-Encoding"),
        };

        var output = new byte[4096];
        var span = output.AsSpan();
        var written = encoder.Encode(typicalHeaders, ref span);
        _typicalEncoded = output[..written];

        var largeHeaders = new List<HpackHeader>();
        largeHeaders.Add(new(":status", "200"));
        for (var i = 0; i < 30; i++)
        {
            largeHeaders.Add(new($"x-response-header-{i}", $"value-{i}"));
        }

        var largeOutput = new byte[65536];
        var largeSpan = largeOutput.AsSpan();
        var largeEncoder = new HpackEncoder(useHuffman: true);
        var largeWritten = largeEncoder.Encode(largeHeaders, ref largeSpan);
        _largeEncoded = largeOutput[..largeWritten];
    }

    [Benchmark(Baseline = true)]
    public int DecodeTypicalHeaders()
    {
        var headers = _decoder.Decode(_typicalEncoded);
        return headers.Count;
    }

    [Benchmark]
    public int Decode31Headers()
    {
        var headers = _decoder.Decode(_largeEncoded);
        return headers.Count;
    }
}
```

- [ ] **Step 3: Create HuffmanBenchmark**

The `HuffmanCodec` is `internal static class` in namespace `TurboHTTP.Protocol`. It has:
- `static int Encode(ReadOnlySpan<byte> input, Span<byte> output)` — returns bytes written
- `static int Decode(ReadOnlySpan<byte> input, Span<byte> output)` — returns bytes written
- `static int GetMaxEncodedLength(int inputLength)` and `GetMaxDecodedLength(int inputLength)`

```csharp
using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol;

namespace TurboHTTP.MicroBenchmarks.Hpack;

[Config(typeof(MicroBenchmarkConfig))]
public class HuffmanBenchmark
{
    private byte[] _shortInput = null!;
    private byte[] _longInput = null!;
    private byte[] _shortEncoded = null!;
    private byte[] _longEncoded = null!;
    private byte[] _encodeOutput = null!;
    private byte[] _decodeOutput = null!;

    [GlobalSetup]
    public void Setup()
    {
        _shortInput = "application/json"u8.ToArray();
        _longInput = System.Text.Encoding.ASCII.GetBytes(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        _encodeOutput = new byte[HuffmanCodec.GetMaxEncodedLength(_longInput.Length)];
        _decodeOutput = new byte[HuffmanCodec.GetMaxDecodedLength(_longInput.Length)];

        var shortOut = new byte[HuffmanCodec.GetMaxEncodedLength(_shortInput.Length)];
        var shortLen = HuffmanCodec.Encode(_shortInput, shortOut);
        _shortEncoded = shortOut[..shortLen];

        var longOut = new byte[HuffmanCodec.GetMaxEncodedLength(_longInput.Length)];
        var longLen = HuffmanCodec.Encode(_longInput, longOut);
        _longEncoded = longOut[..longLen];
    }

    [Benchmark(Baseline = true)]
    public int EncodeShort()
    {
        return HuffmanCodec.Encode(_shortInput, _encodeOutput);
    }

    [Benchmark]
    public int EncodeLong()
    {
        return HuffmanCodec.Encode(_longInput, _encodeOutput);
    }

    [Benchmark]
    public int DecodeShort()
    {
        return HuffmanCodec.Decode(_shortEncoded, _decodeOutput);
    }

    [Benchmark]
    public int DecodeLong()
    {
        return HuffmanCodec.Decode(_longEncoded, _decodeOutput);
    }
}
```

- [ ] **Step 4: Verify build**

Run: `dotnet build --configuration Release src/TurboHTTP.MicroBenchmarks/TurboHTTP.MicroBenchmarks.csproj`
Expected: Build succeeded

- [ ] **Step 5: Dry-run**

Run: `dotnet run --configuration Release --project src/TurboHTTP.MicroBenchmarks/TurboHTTP.MicroBenchmarks.csproj -- --filter *Hpack*OR*Huffman* --job Dry`
Expected: All 3 classes discovered

- [ ] **Step 6: Commit**

```bash
git add src/TurboHTTP.MicroBenchmarks/Hpack/
git commit -m "feat: add HPACK encoder, decoder, and Huffman benchmarks"
```

---

### Task 8: Pipeline Benchmarks

**Files:**
- Create: `src/TurboHTTP.MicroBenchmarks/Pipeline/EngineFlowBenchmark.cs`
- Create: `src/TurboHTTP.MicroBenchmarks/Pipeline/FeedbackBufferBenchmark.cs`
- Create: `src/TurboHTTP.MicroBenchmarks/Pipeline/VersionDispatchBenchmark.cs`

These benchmarks need an Akka `ActorSystem` and `IMaterializer`. They use `EngineTestBase.CreateFakeConnectionFlow()` from `TurboHTTP.Tests.Shared` for fake transports.

- [ ] **Step 1: Create EngineFlowBenchmark**

This measures `Engine.CreateFlow` composition and single-request throughput through the full pipeline with fake transport.

The `Engine` class (`TurboHTTP.Streams.Engine`) has:
- Constructor: default
- Method: `Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlow(TransportRegistry transports, PipelineDescriptor descriptor, TurboClientOptions? options = null)`

`TransportRegistry` (`TurboHTTP.Streams.TransportRegistry`) has:
- Constructor: default
- Method: `TransportRegistry Register(Version version, Flow<ITransportOutbound, ITransportInbound, NotUsed> flow)` — fluent

`PipelineDescriptor` (`TurboHTTP.Streams.PipelineDescriptor`) has:
- `static readonly PipelineDescriptor Empty`

`EngineTestBase` (`TurboHTTP.Tests.Shared`) provides:
- `static Flow<ITransportOutbound, ITransportInbound, NotUsed> CreateFakeConnectionFlow(Func<byte[]> responseFactory)`

Since `EngineTestBase` has a **static** shared `ActorSystem` (initialized in a static constructor), the benchmark can inherit from it or access the static `Materializer`. However, `EngineTestBase` is abstract and its `Materializer` is `protected static`. The benchmark class should inherit from `EngineTestBase`.

```csharp
using System.Net;
using System.Threading.Channels;
using Akka.Streams;
using Akka.Streams.Dsl;
using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.MicroBenchmarks.Pipeline;

[Config(typeof(MicroBenchmarkConfig))]
public class EngineFlowBenchmark : EngineTestBase
{
    private static readonly byte[] OkResponse =
        "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> _flow = null!;
    private ISourceQueueWithComplete<HttpRequestMessage> _queue = null!;
    private Channel<HttpResponseMessage> _responses = null!;

    [GlobalSetup]
    public void Setup()
    {
        _responses = Channel.CreateUnbounded<HttpResponseMessage>();

        var engine = new Engine();
        var transports = new TransportRegistry()
            .Register(new Version(1, 0), CreateFakeConnectionFlow(() => OkResponse))
            .Register(new Version(1, 1), CreateFakeConnectionFlow(() => OkResponse))
            .Register(new Version(2, 0), CreateFakeConnectionFlow(() => OkResponse))
            .Register(new Version(3, 0), CreateFakeConnectionFlow(() => OkResponse));
        _flow = engine.CreateFlow(transports, PipelineDescriptor.Empty);

        var (queue, _) = Source.Queue<HttpRequestMessage>(16, OverflowStrategy.Backpressure)
            .Via(_flow)
            .ToMaterialized(
                Sink.ForEach<HttpResponseMessage>(r => _responses.Writer.TryWrite(r)),
                Keep.Both)
            .Run(Materializer);

        _queue = queue;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _queue.Complete();
    }

    [Benchmark(Baseline = true)]
    public async Task<HttpStatusCode> SingleRequestRoundtrip()
    {
        await _queue.OfferAsync(new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        });

        var response = await _responses.Reader.ReadAsync();
        return response.StatusCode;
    }
}
```

- [ ] **Step 2: Create FeedbackBufferBenchmark**

Measures the overhead of redirect/retry feedback loops. Uses the same pattern as `FeedbackBufferOptimizationSpec` — sequential fake responses.

```csharp
using System.Net;
using Akka;
using Akka.Streams.Dsl;
using BenchmarkDotNet.Attributes;
using Servus.Akka.Transport;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.MicroBenchmarks.Pipeline;

[Config(typeof(MicroBenchmarkConfig))]
public class FeedbackBufferBenchmark : EngineTestBase
{
    private static byte[] Redirect301(string location) =>
        System.Text.Encoding.Latin1.GetBytes(
            $"HTTP/1.1 301 Moved Permanently\r\nLocation: {location}\r\nContent-Length: 0\r\n\r\n");

    private static byte[] Ok200() =>
        "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK"u8.ToArray();

    private Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> _directFlow = null!;
    private Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> _redirectFlow = null!;

    private static Flow<ITransportOutbound, ITransportInbound, NotUsed> SequentialFlow(params byte[][] responses)
    {
        var index = 0;
        return CreateFakeConnectionFlow(() =>
        {
            var i = Interlocked.Increment(ref index) - 1;
            return i < responses.Length ? responses[i] : responses[^1];
        });
    }

    private static Flow<ITransportOutbound, ITransportInbound, NotUsed> NoOpH2Flow()
        => CreateFakeConnectionFlow(Array.Empty<byte>);

    [GlobalSetup]
    public void Setup()
    {
        var engine = new Engine();

        var directTransports = new TransportRegistry()
            .Register(new Version(1, 0), SequentialFlow(Ok200()))
            .Register(new Version(1, 1), SequentialFlow(Ok200()))
            .Register(new Version(2, 0), NoOpH2Flow())
            .Register(new Version(3, 0), NoOpH2Flow());
        _directFlow = engine.CreateFlow(directTransports, PipelineDescriptor.Empty);

        var redirectTransports = new TransportRegistry()
            .Register(new Version(1, 0), SequentialFlow(Ok200()))
            .Register(new Version(1, 1), SequentialFlow(
                Redirect301("http://example.com/step2"),
                Ok200()))
            .Register(new Version(2, 0), NoOpH2Flow())
            .Register(new Version(3, 0), NoOpH2Flow());
        var redirectDescriptor = new PipelineDescriptor(
            RedirectPolicy: new RedirectPolicy(),
            RetryPolicy: null,
            Expect100Policy: null,
            CompressionPolicy: null,
            CookieJar: null,
            CacheStore: null,
            CachePolicy: null,
            Handlers: []);
        _redirectFlow = engine.CreateFlow(redirectTransports, redirectDescriptor);
    }

    private async Task<HttpResponseMessage> RunSingleAsync(
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> flow,
        HttpRequestMessage request)
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Concat(Source.Never<HttpRequestMessage>())
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(r => tcs.TrySetResult(r)), Materializer);
        return await tcs.Task;
    }

    [Benchmark(Baseline = true)]
    public async Task<HttpStatusCode> DirectResponse()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };
        var response = await RunSingleAsync(_directFlow, request);
        return response.StatusCode;
    }

    [Benchmark]
    public async Task<HttpStatusCode> SingleRedirect()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/origin")
        {
            Version = HttpVersion.Version11
        };
        var response = await RunSingleAsync(_redirectFlow, request);
        return response.StatusCode;
    }
}
```

- [ ] **Step 3: Create VersionDispatchBenchmark**

Measures the overhead of routing requests to the correct protocol engine.

```csharp
using System.Net;
using System.Threading.Channels;
using Akka.Streams;
using Akka.Streams.Dsl;
using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.MicroBenchmarks.Pipeline;

[Config(typeof(MicroBenchmarkConfig))]
public class VersionDispatchBenchmark : EngineTestBase
{
    private static readonly byte[] OkResponse =
        "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private ISourceQueueWithComplete<HttpRequestMessage> _queue = null!;
    private Channel<HttpResponseMessage> _responses = null!;

    [Params("1.0", "1.1")]
    public string HttpVersion { get; set; } = "1.1";

    private Version VersionValue => HttpVersion switch
    {
        "1.0" => new Version(1, 0),
        _ => new Version(1, 1)
    };

    [GlobalSetup]
    public void Setup()
    {
        _responses = Channel.CreateUnbounded<HttpResponseMessage>();

        var engine = new Engine();
        var transports = new TransportRegistry()
            .Register(new Version(1, 0), CreateFakeConnectionFlow(() => OkResponse))
            .Register(new Version(1, 1), CreateFakeConnectionFlow(() => OkResponse))
            .Register(new Version(2, 0), CreateFakeConnectionFlow(() => OkResponse))
            .Register(new Version(3, 0), CreateFakeConnectionFlow(() => OkResponse));
        var flow = engine.CreateFlow(transports, PipelineDescriptor.Empty);

        var (queue, _) = Source.Queue<HttpRequestMessage>(16, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(
                Sink.ForEach<HttpResponseMessage>(r => _responses.Writer.TryWrite(r)),
                Keep.Both)
            .Run(Materializer);

        _queue = queue;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _queue.Complete();
    }

    [Benchmark(Baseline = true)]
    public async Task<HttpStatusCode> DispatchRequest()
    {
        await _queue.OfferAsync(new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = VersionValue
        });

        var response = await _responses.Reader.ReadAsync();
        return response.StatusCode;
    }
}
```

- [ ] **Step 4: Verify build**

Run: `dotnet build --configuration Release src/TurboHTTP.MicroBenchmarks/TurboHTTP.MicroBenchmarks.csproj`
Expected: Build succeeded

- [ ] **Step 5: Dry-run**

Run: `dotnet run --configuration Release --project src/TurboHTTP.MicroBenchmarks/TurboHTTP.MicroBenchmarks.csproj -- --filter *Pipeline* --job Dry`
Expected: All 3 classes discovered

- [ ] **Step 6: Commit**

```bash
git add src/TurboHTTP.MicroBenchmarks/Pipeline/
git commit -m "feat: add pipeline benchmarks (engine flow, feedback buffer, version dispatch)"
```

---

### Task 9: Transport Benchmark

**Files:**
- Create: `src/TurboHTTP.MicroBenchmarks/Transport/ConnectionSetupBenchmark.cs`

- [ ] **Step 1: Create ConnectionSetupBenchmark**

Measures TCP loopback connection establishment latency. Starts a TCP listener in `[GlobalSetup]` and measures the time to open + close a connection per iteration.

```csharp
using System.Net;
using System.Net.Sockets;
using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;

namespace TurboHTTP.MicroBenchmarks.Transport;

[Config(typeof(MicroBenchmarkConfig))]
public class ConnectionSetupBenchmark
{
    private TcpListener _listener = null!;
    private int _port;
    private Task _acceptLoop = null!;
    private CancellationTokenSource _cts = null!;

    [GlobalSetup]
    public void Setup()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _acceptLoop = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    client.Close();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _cts.Cancel();
        _listener.Stop();
        _acceptLoop.Wait(TimeSpan.FromSeconds(5));
        _cts.Dispose();
    }

    [Benchmark(Baseline = true)]
    public async Task TcpLoopbackConnect()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build --configuration Release src/TurboHTTP.MicroBenchmarks/TurboHTTP.MicroBenchmarks.csproj`
Expected: Build succeeded

- [ ] **Step 3: Dry-run**

Run: `dotnet run --configuration Release --project src/TurboHTTP.MicroBenchmarks/TurboHTTP.MicroBenchmarks.csproj -- --filter *Connection* --job Dry`
Expected: Benchmark discovered and completes

- [ ] **Step 4: Commit**

```bash
git add src/TurboHTTP.MicroBenchmarks/Transport/
git commit -m "feat: add TCP loopback connection setup benchmark"
```

---

### Task 10: Wire Up Baseline Comparison in Program.cs

**Files:**
- Modify: `src/TurboHTTP.MicroBenchmarks/Program.cs`

- [ ] **Step 1: Update Program.cs to run baseline comparison after benchmarks**

```csharp
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;
using TurboHTTP.MicroBenchmarks.Internal;

var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

var baselineDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Baselines");
var updateBaseline = args.Contains("--update-baseline", StringComparer.OrdinalIgnoreCase);

foreach (var summary in summaries)
{
    var jsonExport = summary.ResultsDirectoryPath;
    var jsonFiles = Directory.Exists(jsonExport)
        ? Directory.GetFiles(jsonExport, "*-report-full.json")
        : [];

    foreach (var jsonFile in jsonFiles)
    {
        var baselinePath = Path.Combine(baselineDir, Path.GetFileName(jsonFile));
        var baseline = BaselineComparer.LoadBaseline(baselinePath);
        var current = BaselineComparer.LoadBaseline(jsonFile);

        var report = BaselineComparer.Compare(baseline, current);
        Console.WriteLine(report);

        if (updateBaseline || baseline.Count == 0)
        {
            BaselineComparer.SaveBaseline(jsonFile, baselineDir);
            Console.WriteLine($"Baseline updated: {baselinePath}");
        }
    }
}
```

- [ ] **Step 2: Create Baselines directory with .gitkeep**

```bash
mkdir -p src/TurboHTTP.MicroBenchmarks/Baselines
touch src/TurboHTTP.MicroBenchmarks/Baselines/.gitkeep
```

- [ ] **Step 3: Verify build**

Run: `dotnet build --configuration Release src/TurboHTTP.MicroBenchmarks/TurboHTTP.MicroBenchmarks.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/TurboHTTP.MicroBenchmarks/Program.cs src/TurboHTTP.MicroBenchmarks/Baselines/.gitkeep
git commit -m "feat: wire up baseline comparison in Program.cs"
```

---

### Task 11: Full Validation Run

- [ ] **Step 1: Build full solution**

Run: `dotnet build --configuration Release src/TurboHTTP.slnx`
Expected: Build succeeded, no warnings from new project

- [ ] **Step 2: Dry-run all benchmarks**

Run: `dotnet run --configuration Release --project src/TurboHTTP.MicroBenchmarks/TurboHTTP.MicroBenchmarks.csproj -- --filter * --job Dry`
Expected: All benchmark classes discovered and pass:
- Http10DecoderBenchmark (2 benchmarks)
- Http10EncoderBenchmark (2 benchmarks)
- Http11DecoderBenchmark (3 benchmarks)
- Http11EncoderBenchmark (2 benchmarks)
- Http11ChunkedDecoderBenchmark (2 benchmarks)
- Http2FrameDecoderBenchmark (3 benchmarks)
- Http2FrameEncoderBenchmark (2 benchmarks)
- Http2ResponseDecoderBenchmark (1 benchmark)
- HpackEncoderBenchmark (2 benchmarks)
- HpackDecoderBenchmark (2 benchmarks)
- HuffmanBenchmark (4 benchmarks)
- EngineFlowBenchmark (1 benchmark)
- FeedbackBufferBenchmark (2 benchmarks)
- VersionDispatchBenchmark (1 benchmark × 2 params = 2)
- ConnectionSetupBenchmark (1 benchmark)

Total: ~30 benchmark methods

- [ ] **Step 3: Run a single real benchmark to validate output**

Run: `dotnet run --configuration Release --project src/TurboHTTP.MicroBenchmarks/TurboHTTP.MicroBenchmarks.csproj -- --filter *Huffman*`
Expected: Full BenchmarkDotNet output with Median, P95, Allocated columns. JSON export generated in results directory.

- [ ] **Step 4: Verify baseline comparison output**

Check console output for "No baseline found — current results will become the new baseline." on first run. Verify `.json` file saved to `Baselines/`.

- [ ] **Step 5: Final commit**

```bash
git add -A
git commit -m "feat: complete TurboHTTP.MicroBenchmarks project with all benchmarks"
```
