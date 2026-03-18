# Plan 7: Architecture Diagrams — C4 Component + Akka.Streams Graph Visualization

## Introduction

Create a set of architecture diagrams for TurboHttp that serve two purposes: (1) a C4 Level 3 (Component) diagram showing the internal structure of the library across its four layers, and (2) an Akka.Streams-style graph visualization of the full Engine pipeline — similar to the [Akka.NET compose_graph_partial](https://getakka.net/images/compose_graph_partial.png) reference image — showing all stages, merges, partitions, and feedback loops. All diagrams use Mermaid Markdown for easy rendering in GitHub and IDEs. Target audience is external library users and architecture reviewers.

## Goals

- Produce a C4 Level 3 (Component) diagram of TurboHttp's four-layer architecture in Mermaid
- Produce a full Engine stream graph showing all 26+ stages, feedback loops, fan-out/fan-in shapes, and data flow direction
- Make diagrams accurate to the current codebase (not aspirational)
- Keep diagrams maintainable — one source file per diagram, stored in `docs/`
- Serve as entry-point documentation for external users and architecture reviews

## User Stories

### TASK-7-001: C4 Level 3 Component Diagram

**Description:** As an external user or reviewer, I want a C4 Component diagram showing TurboHttp's internal structure so that I can understand the library's layered architecture at a glance.

**Acceptance Criteria:**
- [x] Mermaid file created at `docs/c4-components.md`
- [x] Shows four containers/boundaries matching the architecture layers:
  - **Client Layer** — `ITurboHttpClient`, `TurboClientStreamManager`, `TurboClientOptions`
  - **Streams Layer** — `Engine`, `Http10Engine`, `Http11Engine`, `Http20Engine`, all GraphStages grouped by function (encoding, decoding, correlation, business logic)
  - **Protocol Layer** — Encoders (`Http10Encoder`, `Http11Encoder`, `Http2RequestEncoder`), Decoders (`Http10Decoder`, `Http11Decoder`), HPACK (`HpackEncoder`, `HpackDecoder`, `HpackDynamicTable`, `HuffmanCodec`), Business Logic (`RedirectHandler`, `RetryEvaluator`, `CookieJar`, `ConnectionReuseEvaluator`, `ContentEncodingDecoder`), Caching (`HttpCacheStore`, `CacheFreshnessEvaluator`, `CacheValidationRequestBuilder`, `CacheControlParser`)
  - **I/O Layer** — `PoolRouterActor`, `HostPoolActor`, `ConnectionActor`, `ClientByteMover`, `ClientState`
- [x] Dependencies between components shown as arrows with labels (e.g., "encodes requests", "manages TCP connections")
- [x] External system shown: TCP Network
- [x] Diagram renders correctly in GitHub Markdown preview
- [x] Components match actual class names in the codebase

### TASK-7-002: Full Engine Stream Graph

**Description:** As an external user or reviewer, I want an Akka.Streams-style graph diagram of the complete `Engine.BuildExtendedPipeline` so that I can understand the full request/response data flow, including feedback loops.

**Acceptance Criteria:**
- [x] Mermaid file created at `docs/engine-stream-graph.md`
- [x] Shows the complete pipeline from `Engine.cs` `BuildExtendedPipeline`:
  - **Request chain:** RequestEnricherStage -> MergePreferred (redirect feedback) -> CookieInjectionStage -> MergePreferred (retry feedback) -> CacheLookupStage (fan-out: miss/hit)
  - **Engine core:** Partition (4-way by HTTP version) -> Http10Engine / Http11Engine / Http20Engine / Http30Engine -> Merge (4-way)
  - **Response chain:** DecompressionStage -> CookieStorageStage -> CacheStorageStage -> RetryStage (fan-out: final/retry) -> Merge (cache hit + final) -> RedirectStage (fan-out: final/redirect)
  - **Feedback loops:** RetryStage.Out1 -> Buffer(1) -> retryMerge.Preferred; RedirectStage.Out1 -> Buffer(1) -> redirectMerge.Preferred
- [x] Each stage is labeled with its class name
- [x] Fan-out stages show labeled outlets (e.g., "cache miss", "cache hit", "retry", "final", "redirect")
- [x] Buffer stages on feedback loops shown explicitly
- [x] MergePreferred inlets labeled (preferred vs. normal)
- [x] Data types on edges shown where helpful (HttpRequestMessage, HttpResponseMessage)
- [x] Diagram renders correctly in GitHub Markdown preview
- [x] Visual style inspired by the Akka.NET reference image (boxes for stages, arrows for flow)

### TASK-7-003: Protocol Engine Sub-Graphs

**Description:** As an external user or reviewer, I want separate stream graph diagrams for each protocol engine so that I can understand the internal structure of HTTP/1.0, HTTP/1.1, and HTTP/2 processing.

**Acceptance Criteria:**
- [x] Mermaid file created at `docs/protocol-engine-graphs.md` containing three sub-diagrams
- [x] **HTTP/1.0 Engine** graph shows:
  - Broadcast(2) -> Http10EncoderStage -> IOutputItem out
  - IInputItem in -> Http10DecoderStage -> Http1XCorrelationStage -> HttpResponseMessage
  - Correlation stage with two inlets (request from Broadcast, response from decoder)
  - Signal outlet -> Sink.Ignore (matches actual code — no FlowSelect wrappers in Http10Engine)
- [x] **HTTP/1.1 Engine** graph shows same structure as HTTP/1.0 but with Http11Encoder/DecoderStage and MergePreferred for signal feedback
- [x] **HTTP/2 Engine** graph shows:
  - StreamIdAllocatorStage -> Request2FrameStage -> Http20ConnectionStage (AppIn) -> Http20EncoderStage -> MergePreferred -> IOutputItem
  - IInputItem -> Http20DecoderStage -> Http20ConnectionStage (ServerIn) -> Http20StreamStage -> HttpResponseMessage
  - Bidirectional flow control via Http20ConnectionStage with 5 ports (AppIn, ServerOut, ServerIn, AppOut, OutletSignal)
- [x] All graphs show the GroupByHostKey / MergeSubstreams wrapping
- [x] Each stage labeled with class name
- [x] Diagrams render correctly in GitHub Markdown preview

### TASK-7-004: Connection Flow Sub-Graph

**Description:** As an external user or reviewer, I want a stream graph of the per-connection flow (`BuildConnectionFlowPublic`) so that I can understand how TCP connections are managed within each substream.

**Acceptance Criteria:**
- [ ] Mermaid file created at `docs/connection-flow-graph.md`
- [ ] Shows the full graph from `Engine.BuildConnectionFlowPublic`:
  - ExtractOptionsStage (fan-out: request stream Out0 -> BidiFlow inlet, ConnectItem Out1 -> Concat)
  - BidiFlow (protocol engine) with two inlets and two outlets
  - Concat(2): ConnectItem (In0) + BidiFlow transport output (In1)
  - MergePreferred: normal data (In0) + signal feedback (Preferred) -> ConnectionStage
  - ConnectionStage -> BidiFlow decode inlet
  - ConnectionReuseStage (fan-out: response Out0, ConnectionReuseItem Out1)
  - Feedback loop: ConnectionReuseItem -> Select -> Buffer(1) -> MergePreferred.Preferred
- [ ] ConnectionStage shown as the bridge to TCP (external boundary)
- [ ] Diagram renders correctly in GitHub Markdown preview

### TASK-7-005: I/O Actor Hierarchy Diagram

**Description:** As an external user or reviewer, I want a diagram showing the Akka actor hierarchy in the I/O layer so that I can understand the supervision tree and message flow for TCP connection management.

**Acceptance Criteria:**
- [ ] Mermaid file created at `docs/io-actor-hierarchy.md`
- [ ] Shows actor supervision tree:
  - ActorSystem -> PoolRouterActor -> HostPoolActor (per host:port:scheme) -> ConnectionActor (per TCP connection)
- [ ] Shows message flow between actors (ConnectRequest, ConnectionHandle, Disconnect, etc.)
- [ ] Shows the bridge between ConnectionStage (Akka.Streams) and ConnectionActor (Akka actors) via System.Threading.Channels
- [ ] ClientByteMover and ClientState shown as non-actor components owned by ConnectionActor
- [ ] Diagram renders correctly in GitHub Markdown preview

### TASK-7-006: Validation Against Codebase

**Description:** As a developer, I want all diagrams to be verified against the actual source code so that no stage, actor, or connection is missing or incorrectly represented.

**Acceptance Criteria:**
- [ ] Each stage in `src/TurboHttp/Streams/Stages/` appears in at least one diagram
- [ ] Each actor in `src/TurboHttp/IO/` appears in the actor hierarchy diagram
- [ ] Graph wiring in diagrams matches `Engine.cs` `BuildExtendedPipeline` and `BuildConnectionFlowPublic`
- [ ] Protocol engine diagrams match `Http10Engine.cs`, `Http11Engine.cs`, `Http20Engine.cs`
- [ ] No invented/aspirational components — only what exists in code today
- [ ] A checklist in `docs/diagram-validation.md` maps each diagram element to its source file and line number

## Functional Requirements

- FR-1: All diagrams must use Mermaid Markdown syntax exclusively
- FR-2: C4 Component diagram must use Mermaid's `C4Context` or `flowchart` with C4-style grouping (subgraph boundaries for each layer)
- FR-3: Stream graphs must show directionality (left-to-right or top-to-bottom flow)
- FR-4: Fan-out stages (Partition, Broadcast, FanOutShape) must show all outlets with labels
- FR-5: Fan-in stages (Merge, MergePreferred, Concat) must show all inlets, with preferred inlets marked
- FR-6: Feedback loops must be visually distinct (e.g., dashed lines, different color, or explicit "feedback" label)
- FR-7: Buffer stages on feedback loops must be shown as separate nodes (not hidden)
- FR-8: Each diagram file must include a brief text description above the Mermaid block explaining what it shows
- FR-9: All diagrams must reflect the current codebase state — including limitations noted in CLAUDE.md (e.g., "pipeline not fully wired", "client graph not materialized")

## Non-Goals

- No interactive diagrams or web-based visualization tools
- No PlantUML, D2, or Structurizr output — Mermaid only
- No C4 Level 1 (System Context) or Level 2 (Container) — Level 3 only
- No C4 Level 4 (Code) — class diagrams are out of scope
- No sequence diagrams for runtime behavior
- No auto-generation tooling (diagrams are hand-crafted from code inspection)
- No benchmark or performance data in diagrams

## Design Considerations

### Mermaid Limitations
- Mermaid `flowchart` is best for stream graphs (supports subgraphs, labeled edges, shapes)
- Mermaid does not have native C4 support like PlantUML — use `flowchart` with `subgraph` blocks styled as C4 boundaries
- For complex graphs (Engine has 15+ nodes), use `flowchart TD` (top-down) for the main pipeline and `flowchart LR` (left-right) for protocol engines
- Fan-out/fan-in with multiple labeled edges works well in Mermaid
- Feedback loops render better with explicit `linkStyle` for dashed lines

### Visual Style Reference
- The [Akka.NET compose_graph_partial](https://getakka.net/images/compose_graph_partial.png) shows: rounded boxes for stages, arrows with port labels, subgraph boundaries for composite flows
- Adapt this style: rounded boxes (`([Stage])` or `[Stage]`), labeled arrows (`-->|"label"|`), subgraphs for engine boundaries

### File Organization
```
docs/
  c4-components.md          -- C4 Level 3 Component diagram
  engine-stream-graph.md    -- Full Engine pipeline graph
  protocol-engine-graphs.md -- HTTP/1.0, 1.1, 2.0 engine sub-graphs
  connection-flow-graph.md  -- Per-connection flow graph
  io-actor-hierarchy.md     -- Actor supervision tree
  diagram-validation.md     -- Traceability checklist
```

## Technical Considerations

- Mermaid rendering varies slightly between GitHub, VS Code (Markdown Preview Mermaid Support extension), and other tools — test in GitHub preview as primary target
- Very large Mermaid graphs may hit rendering limits — if the full Engine graph is too large for a single diagram, split into "request chain", "engine core", and "response chain" sub-diagrams within the same file
- Use consistent node ID naming convention: `stage_camelCase` (e.g., `cookieInject`, `retryMerge`, `http11Enc`)

## Success Metrics

- All 6 diagram files render correctly in GitHub Markdown preview
- A new contributor can understand the full request lifecycle by reading the Engine stream graph
- Every GraphStage and Actor in the codebase is represented in at least one diagram
- Diagram-to-code traceability is complete (validation checklist has no gaps)

## Open Questions

1. Should the diagrams include the `TurboClientStreamManager` materialization (Source.Queue -> Engine -> Sink.ForEach), or start at Engine level?
2. Should `PerHostConnectionLimiter` appear in the diagrams even though it's not yet wired into a stage?
3. For HTTP/2, should HPACK encoder/decoder be shown as sub-components within `Http20StreamStage` / `Request2FrameStage`, or kept at Protocol layer only?
4. Should we add a legend/key explaining Mermaid shape conventions (rounded = stage, diamond = decision, etc.)?
