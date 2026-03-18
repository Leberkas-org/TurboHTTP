# TurboHttp — I/O Actor Hierarchy & Data Path

## Actor Supervision Tree

```mermaid
graph TD
    subgraph ActorSystem["Akka Actor System"]
        PR["PoolRouterActor<br/><i>Routes EnsureHost to per-host pools</i>"]

        PR -->|"supervises (one per host)"| HPA1["HostPoolActor<br/>[host-a:443]<br/><i>Pool, eviction, MRU selection</i>"]
        PR -->|"supervises (one per host)"| HPA2["HostPoolActor<br/>[host-b:80]<br/><i>Pool, eviction, MRU selection</i>"]

        HPA1 -->|"supervises (max 6)"| CA1["ConnectionActor #1"]
        HPA1 -->|"supervises (max 6)"| CA2["ConnectionActor #2"]
        HPA2 -->|"supervises (max 6)"| CA3["ConnectionActor #3"]

        CM["ClientManager<br/><i>Factory for ClientRunner</i>"]
        CA1 -->|"CreateRunnerWithChannels"| CM
        CA2 -->|"CreateRunnerWithChannels"| CM
        CA3 -->|"CreateRunnerWithChannels"| CM

        CM -->|"resolves child"| CR1["ClientRunner #1<br/><i>Owns TCP lifecycle</i>"]
        CM -->|"resolves child"| CR2["ClientRunner #2"]
        CM -->|"resolves child"| CR3["ClientRunner #3"]
    end

    subgraph NonActor["Non-Actor Components<br/>(owned by ClientRunner)"]
        CR1 -.-|"spawns 3 tasks"| CBM1["ClientByteMover<br/><i>3 async pump tasks</i>"]
        CR1 -.-|"creates"| CS1["ClientState<br/><i>Stream + Pipe + Channels</i>"]
        CR1 -.-|"uses"| ICP1["IClientProvider<br/><i>TCP or TLS socket</i>"]
    end

    style ActorSystem fill:#1a1a2e,stroke:#e94560,color:#eee
    style NonActor fill:#0f3460,stroke:#16213e,color:#eee
    style PR fill:#e94560,stroke:#fff,color:#fff
    style HPA1 fill:#533483,stroke:#fff,color:#fff
    style HPA2 fill:#533483,stroke:#fff,color:#fff
    style CA1 fill:#0f3460,stroke:#fff,color:#fff
    style CA2 fill:#0f3460,stroke:#fff,color:#fff
    style CA3 fill:#0f3460,stroke:#fff,color:#fff
    style CM fill:#16213e,stroke:#fff,color:#fff
    style CR1 fill:#16213e,stroke:#fff,color:#fff
    style CR2 fill:#16213e,stroke:#fff,color:#fff
    style CR3 fill:#16213e,stroke:#fff,color:#fff
    style CBM1 fill:#1a1a2e,stroke:#e94560,color:#eee
    style CS1 fill:#1a1a2e,stroke:#e94560,color:#eee
    style ICP1 fill:#1a1a2e,stroke:#e94560,color:#eee
```

## Actor Message Flow

```mermaid
sequenceDiagram
    participant CS as ConnectionStage<br/>(Akka.Streams)
    participant PR as PoolRouterActor
    participant HPA as HostPoolActor
    participant CA as ConnectionActor
    participant CR as ClientRunner
    participant CBM as ClientByteMover

    Note over CS,CBM: Connection Establishment
    CS->>PR: EnsureHost(Key, TcpOptions)
    PR->>HPA: EnsureHost (forward, preserve sender)
    HPA->>HPA: SelectConnection() → null (first request)
    HPA->>CA: SpawnConnection() → new ConnectionActor
    CA->>CR: CreateRunnerWithChannels(Options, Channels)
    CR->>CR: TCP connect + TLS handshake
    CR->>CA: ClientConnected(EndPoint, InboundReader, OutboundWriter)
    CA->>HPA: ConnectionReady(ConnectionHandle)
    HPA->>CS: ConnectionHandle (reply to queued sender)

    Note over CS,CBM: Data Transfer (zero actor hops)
    CS-->>CBM: OutboundWriter → [Channel] → MoveChannelToStream → TCP
    CBM-->>CS: TCP → MoveStreamToPipe → MovePipeToChannel → [Channel] → InboundReader

    Note over CS,CBM: Stream Lifecycle Signals
    CS->>CA: StreamCompleted(Connection)
    CA->>HPA: StreamCompleted(Connection)
    HPA->>HPA: MarkIdle, ServeQueuedRequesters()

    CS->>CA: MarkConnectionNoReuse(Connection)
    CA->>HPA: MarkConnectionNoReuse(Connection)

    Note over CS,CBM: Disconnect & Reconnect
    CR->>CA: ClientDisconnected(EndPoint)
    CA->>HPA: ConnectionFailed(Connection)
    HPA->>HPA: Schedule Reconnect (exponential backoff)
    HPA->>CA: Reconnect → SpawnConnection()
```

## ConnectionStage ↔ Actor Bridge via System.Threading.Channels

```mermaid
graph LR
    subgraph AkkaStreams["Akka.Streams Pipeline"]
        ENC["Encoder Stage<br/>(Http1x/Http2)"]
        CSTAGE["ConnectionStage<br/><i>GraphStage bridge</i>"]
        DEC["Decoder Stage<br/>(Http1x/Http2)"]
        ENC -->|"IOutputItem<br/>(DataItem, ConnectItem,<br/>StreamAcquireItem, ...)"| CSTAGE
        CSTAGE -->|"IInputItem<br/>(DataItem)"| DEC
    end

    subgraph ActorPath["Actor Lifecycle Path"]
        SA["StageActor<br/><i>(ConnectionStage's actor ref)</i>"]
        PRA["PoolRouterActor"]
        HPAA["HostPoolActor"]
        CAA["ConnectionActor"]
    end

    subgraph ChannelPath["Channel Data Path (zero actor hops)"]
        OCH["Outbound Channel<br/><code>Channel&lt;(IMemoryOwner, int)&gt;</code>"]
        ICH["Inbound Channel<br/><code>Channel&lt;(IMemoryOwner, int)&gt;</code>"]
        PIPE["System.IO.Pipelines.Pipe<br/><i>Buffer between socket and channel</i>"]
        SOCK["TCP / TLS Socket"]
    end

    subgraph ByteMover["ClientByteMover (3 async tasks)"]
        T1["Task 1: MoveStreamToPipe<br/><i>Socket → Pipe</i>"]
        T2["Task 2: MovePipeToChannel<br/><i>Pipe → Inbound Channel</i>"]
        T3["Task 3: MoveChannelToStream<br/><i>Outbound Channel → Socket</i>"]
    end

    %% Actor lifecycle messages
    CSTAGE -.->|"ConnectItem →<br/>EnsureHost"| SA
    SA -.->|"EnsureHost"| PRA
    PRA -.->|"forward"| HPAA
    HPAA -.->|"ConnectionReady(Handle)"| SA
    SA -.->|"ConnectionHandle"| CSTAGE

    %% Control signals via ConnectionActor
    CSTAGE -.->|"StreamCompleted,<br/>MarkNoReuse"| CAA
    CAA -.->|"forward"| HPAA

    %% Data path: outbound
    CSTAGE -->|"write bytes"| OCH
    OCH --> T3
    T3 --> SOCK

    %% Data path: inbound
    SOCK --> T1
    T1 --> PIPE
    PIPE --> T2
    T2 --> ICH
    ICH -->|"read bytes"| CSTAGE

    %% ConnectionHandle bundles channel refs
    CSTAGE -.->|"holds ConnectionHandle<br/>(OutboundWriter,<br/>InboundReader,<br/>Key, ActorRef)"| OCH
    CSTAGE -.->|"holds ConnectionHandle"| ICH

    style AkkaStreams fill:#1a1a2e,stroke:#e94560,color:#eee
    style ActorPath fill:#533483,stroke:#fff,color:#eee
    style ChannelPath fill:#0f3460,stroke:#16213e,color:#eee
    style ByteMover fill:#16213e,stroke:#e94560,color:#eee
    style CSTAGE fill:#e94560,stroke:#fff,color:#fff
    style SA fill:#533483,stroke:#fff,color:#fff
    style OCH fill:#0f3460,stroke:#fff,color:#fff
    style ICH fill:#0f3460,stroke:#fff,color:#fff
    style PIPE fill:#0f3460,stroke:#fff,color:#fff
```

## ConnectionHandle — Data Path Bridge

`ConnectionHandle` is the key data structure that enables zero-actor-hop data transfer. It bundles direct channel references, bypassing actor mailboxes entirely for request/response bytes:

| Field | Type | Purpose |
|-------|------|---------|
| `OutboundWriter` | `ChannelWriter<(IMemoryOwner<byte>, int)>` | ConnectionStage writes serialized request bytes here |
| `InboundReader` | `ChannelReader<(IMemoryOwner<byte>, int)>` | ConnectionStage reads inbound response bytes from here |
| `Key` | `RequestEndpoint` | Connection identity: Scheme + Host + Port + Version |
| `ConnectionActor` | `IActorRef` | Owning actor for lifecycle message forwarding |
| `MaxConcurrentStreams` | `int` (volatile) | Updated by Http20ConnectionStage on SETTINGS frame |

## ClientByteMover — Three Concurrent Pump Tasks

Each TCP connection spawns three independent async tasks (no actor involvement):

| Task | Flow | Trigger on Failure |
|------|------|--------------------|
| `MoveStreamToPipe` | TCP Socket → `Pipe.Writer` | Tells runner `DoClose` |
| `MovePipeToChannel` | `Pipe.Reader` → Inbound `ChannelWriter` | Tells runner `DoClose` |
| `MoveChannelToStream` | Outbound `ChannelReader` → TCP Socket | Tells runner `DoClose` |

Any task failure triggers `ClientRunner.DoClose`, which propagates to `ConnectionActor` as `ClientDisconnected`, initiating reconnect with exponential backoff.

## ClientState — Per-Connection I/O Primitives

```
ClientState
├── Stream          — NetworkStream or SslStream (from IClientProvider)
├── Pipe            — System.IO.Pipelines buffer (pause/resume thresholds scale with MaxFrameSize)
├── InboundReader   — ChannelReader  (ClientByteMover writes, ConnectionStage reads)
├── InboundWriter   — ChannelWriter  (ClientByteMover writes here from Pipe)
├── OutboundReader  — ChannelReader  (ClientByteMover reads, sends to socket)
└── OutboundWriter  — ChannelWriter  (ConnectionStage writes here via ConnectionHandle)
```
