# Entity Routing Fluent API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add callback-based `IsAsk()`/`IsTell()` methods to `MapTurboEntity` with per-endpoint response handlers and `IResult`/`TypedResults` support.

**Architecture:** Sub-builders (`TurboEntityAskBuilder`, `TurboEntityTellBuilder`) hold configuration state, exposed via `Action<T>` callbacks from `TurboEntityMethodBuilder.IsAsk()`/`.IsTell()`. Both Ask `Response<T>` and `Produces<T>` compile into `EntityResponseMapperCollection` entries. Tell handlers compile into `Func<TurboHttpContext, Task>`. The `EntityDispatcher` checks per-endpoint mappers first, falls back to entity-level.

**Tech Stack:** C# 12, xUnit v3 (dotnet run), ASP.NET `IResult`/`TypedResults`

**Spec:** `docs/superpowers/specs/2026-05-20-entity-routing-fluent-api-design.md`

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/TurboHTTP/Routing/EntityMethodConfig.cs` | Modify | Add `EndpointMappers` and `TellResponseHandler` parameters |
| `src/TurboHTTP/Routing/EntityResponseMapperCollection.cs` | Modify | Add `Count` property |
| `src/TurboHTTP/Server/TurboEntityTellBuilder.cs` | Create | Tell builder: `Response(int)`, `Response(int, writer)`, `Produces(factory)` |
| `src/TurboHTTP/Server/TurboEntityAskBuilder.cs` | Create | Ask builder: `Response<T>`, `Produces<T>`, `WithTimeout` |
| `src/TurboHTTP/Server/TurboEntityMethodBuilder.cs` | Modify | Add `IsAsk()`, `IsTell()`, deprecate `AcceptedResponse()` |
| `src/TurboHTTP/Routing/EntityDispatcher.cs` | Modify | Two-tier mapper lookup, pluggable Tell handler |
| `src/TurboHTTP.Tests/Routing/TurboEntityTellBuilderSpec.cs` | Create | Tests for Tell builder |
| `src/TurboHTTP.Tests/Routing/TurboEntityAskBuilderSpec.cs` | Create | Tests for Ask builder |
| `src/TurboHTTP.Tests/Routing/TurboEntityBuilderSpec.cs` | Modify | Add IsAsk/IsTell integration tests |
| `src/TurboHTTP.Tests/Routing/EntityResponseMapperCollectionSpec.cs` | Modify | Add Count test |

---

### Task 1: Extend EntityMethodConfig

**Files:**
- Modify: `src/TurboHTTP/Routing/EntityMethodConfig.cs`
- Modify: `src/TurboHTTP/Server/TurboEntityMethodBuilder.cs` (update `ToConfig()` call)

- [ ] **Step 1: Update EntityMethodConfig record**

Replace the full contents of `src/TurboHTTP/Routing/EntityMethodConfig.cs`:

```csharp
using TurboHTTP.Server;

namespace TurboHTTP.Routing;

internal sealed record EntityMethodConfig(
    Func<TurboHttpContext, IServiceProvider, ValueTask<object>> MessageFactory,
    bool IsTell,
    TimeSpan? TimeoutOverride,
    EntityResponseMapperCollection? EndpointMappers,
    Func<TurboHttpContext, Task>? TellResponseHandler);
```

- [ ] **Step 2: Update ToConfig() in TurboEntityMethodBuilder**

In `src/TurboHTTP/Server/TurboEntityMethodBuilder.cs`, change the `ToConfig()` method to pass the new parameters (null for now — wired in Task 5):

```csharp
internal EntityMethodConfig ToConfig() => new(MessageFactory, IsTell, TimeoutOverride, null, null);
```

- [ ] **Step 3: Verify build**

Run: `dotnet build --configuration Release src/TurboHTTP.slnx`
Expected: Build succeeds with 0 errors.

- [ ] **Step 4: Run existing tests to confirm nothing breaks**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Routing.TurboEntityBuilderSpec"`
Expected: All 7 existing tests pass.

- [ ] **Step 5: Commit**

```
feat(routing): extend EntityMethodConfig with endpoint mappers and tell handler
```

---

### Task 2: Add Count to EntityResponseMapperCollection

**Files:**
- Modify: `src/TurboHTTP/Routing/EntityResponseMapperCollection.cs`
- Modify: `src/TurboHTTP.Tests/Routing/EntityResponseMapperCollectionSpec.cs`

- [ ] **Step 1: Write the failing test**

Add to the end of `EntityResponseMapperCollectionSpec`:

```csharp
[Fact(Timeout = 5000)]
public void Count_should_reflect_registered_mappers()
{
    var collection = new EntityResponseMapperCollection();
    Assert.Equal(0, collection.Count);

    collection.Add<OrderResult>((_, _) => Task.CompletedTask);
    Assert.Equal(1, collection.Count);

    collection.Add<DerivedResult>((_, _) => Task.CompletedTask);
    Assert.Equal(2, collection.Count);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Routing.EntityResponseMapperCollectionSpec" -method "Count_should_reflect_registered_mappers"`
Expected: FAIL — `Count` property does not exist.

- [ ] **Step 3: Add Count property**

In `src/TurboHTTP/Routing/EntityResponseMapperCollection.cs`, add after the `_mappers` field:

```csharp
internal int Count => _mappers.Count;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Routing.EntityResponseMapperCollectionSpec"`
Expected: All 5 tests pass (4 existing + 1 new).

- [ ] **Step 5: Commit**

```
feat(routing): add Count property to EntityResponseMapperCollection
```

---

### Task 3: Create TurboEntityTellBuilder

**Files:**
- Create: `src/TurboHTTP/Server/TurboEntityTellBuilder.cs`
- Create: `src/TurboHTTP.Tests/Routing/TurboEntityTellBuilderSpec.cs`

- [ ] **Step 1: Write the failing tests**

Create `src/TurboHTTP.Tests/Routing/TurboEntityTellBuilderSpec.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Routing;

public sealed class TurboEntityTellBuilderSpec
{
    [Fact(Timeout = 5000)]
    public void ResponseHandler_should_be_null_by_default()
    {
        var builder = new TurboEntityTellBuilder();

        Assert.Null(builder.ResponseHandler);
    }

    [Fact(Timeout = 5000)]
    public async Task Response_with_status_code_should_set_status()
    {
        var builder = new TurboEntityTellBuilder();
        builder.Response(204);

        Assert.NotNull(builder.ResponseHandler);

        var ctx = TestContextFactory.Create();
        await builder.ResponseHandler!(ctx);
        Assert.Equal(204, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Response_with_writer_should_set_status_and_invoke_writer()
    {
        var writerInvoked = false;
        var builder = new TurboEntityTellBuilder();
        builder.Response(202, ctx =>
        {
            writerInvoked = true;
            return Task.CompletedTask;
        });

        Assert.NotNull(builder.ResponseHandler);

        var ctx = TestContextFactory.Create();
        await builder.ResponseHandler!(ctx);
        Assert.Equal(202, ctx.Response.StatusCode);
        Assert.True(writerInvoked);
    }

    [Fact(Timeout = 5000)]
    public async Task Produces_should_execute_iresult()
    {
        var resultExecuted = false;
        var builder = new TurboEntityTellBuilder();
        builder.Produces(ctx =>
        {
            resultExecuted = true;
            return new TestResult(201);
        });

        Assert.NotNull(builder.ResponseHandler);

        var ctx = TestContextFactory.Create();
        await builder.ResponseHandler!(ctx);
        Assert.True(resultExecuted);
        Assert.Equal(201, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Last_call_should_win()
    {
        var builder = new TurboEntityTellBuilder();
        builder.Response(204);
        builder.Response(409);

        var ctx = TestContextFactory.Create();
        await builder.ResponseHandler!(ctx);
        Assert.Equal(409, ctx.Response.StatusCode);
    }

    private sealed class TestResult(int statusCode) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }
    }
}
```

This test file references `TestContextFactory.Create()` — a minimal helper that returns a `TurboHttpContext` for testing. We need to create this too. Check if one exists already; if not, create a shared helper.

- [ ] **Step 2: Create TestContextFactory**

First check whether a test context factory already exists in the test project. Search for `TestContextFactory` or any existing helper that creates `TurboHttpContext` instances for tests. If none exists, create `src/TurboHTTP.Tests/Routing/TestContextFactory.cs`:

```csharp
using Akka.Streams;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Routing;

internal static class TestContextFactory
{
    public static TurboHttpContext Create()
    {
        var features = new FeatureCollection();
        features.Set<IHttpResponseFeature>(new HttpResponseFeature());
        features.Set<IHttpRequestFeature>(new HttpRequestFeature());

        return new TurboHttpContext(
            features,
            new TurboConnectionInfo(),
            services: null,
            requestAborted: CancellationToken.None,
            materializer: null!);
    }
}
```

Note: `TurboConnectionInfo` may have required constructor parameters. Check its constructor and adjust accordingly. The materializer can be `null!` since tests don't use Akka streams.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Routing.TurboEntityTellBuilderSpec"`
Expected: FAIL — `TurboEntityTellBuilder` does not exist.

- [ ] **Step 4: Implement TurboEntityTellBuilder**

Create `src/TurboHTTP/Server/TurboEntityTellBuilder.cs`:

```csharp
using Microsoft.AspNetCore.Http;

namespace TurboHTTP.Server;

public sealed class TurboEntityTellBuilder
{
    internal Func<TurboHttpContext, Task>? ResponseHandler { get; private set; }

    public TurboEntityTellBuilder Response(int statusCode)
    {
        ResponseHandler = ctx =>
        {
            ctx.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        };
        return this;
    }

    public TurboEntityTellBuilder Response(int statusCode, Func<TurboHttpContext, Task> writer)
    {
        ResponseHandler = async ctx =>
        {
            ctx.Response.StatusCode = statusCode;
            await writer(ctx);
        };
        return this;
    }

    public TurboEntityTellBuilder Produces(Func<TurboHttpContext, IResult> factory)
    {
        ResponseHandler = async ctx => await factory(ctx).ExecuteAsync(ctx);
        return this;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Routing.TurboEntityTellBuilderSpec"`
Expected: All 5 tests pass.

- [ ] **Step 6: Commit**

```
feat(server): add TurboEntityTellBuilder with Response and Produces support
```

---

### Task 4: Create TurboEntityAskBuilder

**Files:**
- Create: `src/TurboHTTP/Server/TurboEntityAskBuilder.cs`
- Create: `src/TurboHTTP.Tests/Routing/TurboEntityAskBuilderSpec.cs`

- [ ] **Step 1: Write the failing tests**

Create `src/TurboHTTP.Tests/Routing/TurboEntityAskBuilderSpec.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Routing;

public sealed class TurboEntityAskBuilderSpec
{
    private sealed record OrderDto(string Id);

    private sealed record NotFoundResult;

    [Fact(Timeout = 5000)]
    public void Mappers_should_be_empty_by_default()
    {
        var builder = new TurboEntityAskBuilder();

        Assert.Equal(0, builder.Mappers.Count);
    }

    [Fact(Timeout = 5000)]
    public void Response_should_register_mapper()
    {
        var builder = new TurboEntityAskBuilder();
        builder.Response<OrderDto>((ctx, order) => Task.CompletedTask);

        Assert.Equal(1, builder.Mappers.Count);
    }

    [Fact(Timeout = 5000)]
    public async Task Response_should_invoke_handler_with_typed_response()
    {
        var capturedId = "";
        var builder = new TurboEntityAskBuilder();
        builder.Response<OrderDto>((ctx, order) =>
        {
            capturedId = order.Id;
            return Task.CompletedTask;
        });

        var mapper = builder.Mappers.FindMapper(typeof(OrderDto));
        Assert.NotNull(mapper);
        await mapper(null!, new OrderDto("42"));
        Assert.Equal("42", capturedId);
    }

    [Fact(Timeout = 5000)]
    public void Produces_should_register_mapper()
    {
        var builder = new TurboEntityAskBuilder();
        builder.Produces<OrderDto>((ctx, order) => new TestResult(200));

        Assert.Equal(1, builder.Mappers.Count);
    }

    [Fact(Timeout = 5000)]
    public async Task Produces_should_execute_iresult()
    {
        var resultExecuted = false;
        var builder = new TurboEntityAskBuilder();
        builder.Produces<OrderDto>((ctx, order) =>
        {
            resultExecuted = true;
            return new TestResult(200);
        });

        var mapper = builder.Mappers.FindMapper(typeof(OrderDto));
        Assert.NotNull(mapper);

        var ctx = TestContextFactory.Create();
        await mapper(ctx, new OrderDto("1"));
        Assert.True(resultExecuted);
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void Response_and_Produces_should_coexist()
    {
        var builder = new TurboEntityAskBuilder();
        builder.Response<OrderDto>((ctx, order) => Task.CompletedTask);
        builder.Produces<NotFoundResult>((ctx, _) => new TestResult(404));

        Assert.Equal(2, builder.Mappers.Count);
        Assert.NotNull(builder.Mappers.FindMapper(typeof(OrderDto)));
        Assert.NotNull(builder.Mappers.FindMapper(typeof(NotFoundResult)));
    }

    [Fact(Timeout = 5000)]
    public void WithTimeout_should_set_override()
    {
        var builder = new TurboEntityAskBuilder();
        builder.WithTimeout(TimeSpan.FromSeconds(42));

        Assert.Equal(TimeSpan.FromSeconds(42), builder.TimeoutOverride);
    }

    [Fact(Timeout = 5000)]
    public void TimeoutOverride_should_be_null_by_default()
    {
        var builder = new TurboEntityAskBuilder();

        Assert.Null(builder.TimeoutOverride);
    }

    private sealed class TestResult(int statusCode) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Routing.TurboEntityAskBuilderSpec"`
Expected: FAIL — `TurboEntityAskBuilder` does not exist.

- [ ] **Step 3: Implement TurboEntityAskBuilder**

Create `src/TurboHTTP/Server/TurboEntityAskBuilder.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using TurboHTTP.Routing;

namespace TurboHTTP.Server;

public sealed class TurboEntityAskBuilder
{
    internal EntityResponseMapperCollection Mappers { get; } = new();
    internal TimeSpan? TimeoutOverride { get; private set; }

    public TurboEntityAskBuilder Response<TResponse>(Func<TurboHttpContext, TResponse, Task> handler)
    {
        Mappers.Add(handler);
        return this;
    }

    public TurboEntityAskBuilder Produces<TResponse>(Func<TurboHttpContext, TResponse, IResult> handler)
    {
        Mappers.Add<TResponse>(async (ctx, resp) => await handler(ctx, resp).ExecuteAsync(ctx));
        return this;
    }

    public TurboEntityAskBuilder WithTimeout(TimeSpan timeout)
    {
        TimeoutOverride = timeout;
        return this;
    }
}
```

Note on `Produces<T>`: The `EntityResponseMapperCollection.Add<T>` method takes `Func<TurboHttpContext, T, Task>`. The wrapper must cast `resp` from `T` properly. Check the existing `Add<T>` signature and adjust. Looking at the collection:

```csharp
public void Add<T>(Func<TurboHttpContext, T, Task> mapper)
{
    _mappers.Add((typeof(T), (ctx, obj) => mapper(ctx, (T)obj)));
}
```

So `Produces<T>` needs to be:

```csharp
public TurboEntityAskBuilder Produces<TResponse>(Func<TurboHttpContext, TResponse, IResult> handler)
{
    Mappers.Add<TResponse>(async (ctx, resp) => await handler(ctx, resp).ExecuteAsync(ctx));
    return this;
}
```

This works because `Add<TResponse>` takes `Func<TurboHttpContext, TResponse, Task>`, and `async (ctx, resp) => await handler(ctx, resp).ExecuteAsync(ctx)` matches that signature.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Routing.TurboEntityAskBuilderSpec"`
Expected: All 8 tests pass.

- [ ] **Step 5: Commit**

```
feat(server): add TurboEntityAskBuilder with Response, Produces, and WithTimeout support
```

---

### Task 5: Wire IsAsk/IsTell into TurboEntityMethodBuilder

**Files:**
- Modify: `src/TurboHTTP/Server/TurboEntityMethodBuilder.cs`
- Modify: `src/TurboHTTP.Tests/Routing/TurboEntityBuilderSpec.cs`

- [ ] **Step 1: Write the failing tests**

Add the following tests to the end of `TurboEntityBuilderSpec`:

```csharp
[Fact(Timeout = 5000)]
public void IsTell_should_set_tell_flag_in_config()
{
    var builder = new TurboEntityBuilder("/orders/{id}");
    builder.OnPost(() => new TestMessage("new")).IsTell();

    var table = new TurboRouteTable();
    builder.AddToRouteTable(table);
    var frozen = table.Freeze();

    Assert.True(frozen.Match(HttpMethod.Post, "/orders/1").IsMatch);
}

[Fact(Timeout = 5000)]
public void IsTell_with_callback_should_register_tell_route()
{
    var builder = new TurboEntityBuilder("/orders/{id}");
    builder.OnPost(() => new TestMessage("new")).IsTell(tell =>
    {
        tell.Response(204);
    });

    var table = new TurboRouteTable();
    builder.AddToRouteTable(table);
    var frozen = table.Freeze();

    Assert.True(frozen.Match(HttpMethod.Post, "/orders/1").IsMatch);
}

[Fact(Timeout = 5000)]
public void IsAsk_should_register_ask_route()
{
    var builder = new TurboEntityBuilder("/orders/{id}");
    builder.OnGet(() => new TestMessage("get")).IsAsk(ask =>
    {
        ask.Response<TestMessage>((ctx, msg) => Task.CompletedTask);
    });

    var table = new TurboRouteTable();
    builder.AddToRouteTable(table);
    var frozen = table.Freeze();

    Assert.True(frozen.Match(HttpMethod.Get, "/orders/1").IsMatch);
}

[Fact(Timeout = 5000)]
public void IsAsk_without_handlers_should_throw()
{
    var builder = new TurboEntityBuilder("/orders/{id}");
    var methodBuilder = builder.OnGet(() => new TestMessage("get"));

    Assert.Throws<InvalidOperationException>(() =>
        methodBuilder.IsAsk(ask => { }));
}

[Fact(Timeout = 5000)]
public void AcceptedResponse_should_still_work()
{
#pragma warning disable CS0618
    var builder = new TurboEntityBuilder("/orders/{id}");
    builder.OnPost(() => new TestMessage("new")).AcceptedResponse();
#pragma warning restore CS0618

    var table = new TurboRouteTable();
    builder.AddToRouteTable(table);
    var frozen = table.Freeze();

    Assert.True(frozen.Match(HttpMethod.Post, "/orders/1").IsMatch);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Routing.TurboEntityBuilderSpec"`
Expected: FAIL — `IsAsk` and `IsTell` methods do not exist.

- [ ] **Step 3: Implement IsAsk/IsTell on TurboEntityMethodBuilder**

Replace `src/TurboHTTP/Server/TurboEntityMethodBuilder.cs` with:

```csharp
using TurboHTTP.Routing;

namespace TurboHTTP.Server;

public sealed class TurboEntityMethodBuilder
{
    private Func<TurboHttpContext, IServiceProvider, ValueTask<object>> MessageFactory { get; }
    private bool _isTell;
    private TimeSpan? _timeoutOverride;
    private EntityResponseMapperCollection? _endpointMappers;
    private Func<TurboHttpContext, Task>? _tellResponseHandler;

    internal TurboEntityMethodBuilder(Func<TurboHttpContext, IServiceProvider, ValueTask<object>> messageFactory)
    {
        MessageFactory = messageFactory;
    }

    public void IsAsk(Action<TurboEntityAskBuilder> configure)
    {
        _isTell = false;
        _endpointMappers = null;
        _tellResponseHandler = null;

        var builder = new TurboEntityAskBuilder();
        configure(builder);

        if (builder.Mappers.Count == 0)
        {
            throw new InvalidOperationException(
                "IsAsk requires at least one Response<T> or Produces<T> handler.");
        }

        _endpointMappers = builder.Mappers;
        _timeoutOverride = builder.TimeoutOverride ?? _timeoutOverride;
    }

    public void IsTell(Action<TurboEntityTellBuilder>? configure = null)
    {
        _isTell = true;
        _endpointMappers = null;

        if (configure is not null)
        {
            var builder = new TurboEntityTellBuilder();
            configure(builder);
            _tellResponseHandler = builder.ResponseHandler;
        }
    }

    [Obsolete("Use .IsTell() instead")]
    public TurboEntityMethodBuilder AcceptedResponse()
    {
        IsTell();
        return this;
    }

    public TurboEntityMethodBuilder WithTimeout(TimeSpan timeout)
    {
        _timeoutOverride = timeout;
        return this;
    }

    internal EntityMethodConfig ToConfig() => new(
        MessageFactory,
        _isTell,
        _timeoutOverride,
        _endpointMappers,
        _tellResponseHandler);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Routing.TurboEntityBuilderSpec"`
Expected: All 12 tests pass (7 existing + 5 new).

- [ ] **Step 5: Commit**

```
feat(server): add IsAsk/IsTell to TurboEntityMethodBuilder, deprecate AcceptedResponse
```

---

### Task 6: Update EntityDispatcher

**Files:**
- Modify: `src/TurboHTTP/Routing/EntityDispatcher.cs`

- [ ] **Step 1: Update ExecuteAsk with two-tier mapper lookup**

In `src/TurboHTTP/Routing/EntityDispatcher.cs`, replace the mapper lookup line in `ExecuteAsk`:

```csharp
// Old:
var mapper = _responseMappers.FindMapper(response.GetType());

// New:
var mapper = _methodConfig.EndpointMappers?.FindMapper(response.GetType())
          ?? _responseMappers.FindMapper(response.GetType());
```

The full `ExecuteAsk` method becomes:

```csharp
private async Task ExecuteAsk(TurboHttpContext ctx, CancellationToken ct)
{
    try
    {
        var timeout = _methodConfig.TimeoutOverride ?? _timeout;
        var actorRef = await ResolveActor(ctx.RequestServices, ct);
        var message = await _methodConfig.MessageFactory(ctx, ctx.RequestServices);
        var response = await actorRef.Ask<object>(message, timeout, ct);

        var mapper = _methodConfig.EndpointMappers?.FindMapper(response.GetType())
                  ?? _responseMappers.FindMapper(response.GetType());

        if (mapper is null)
        {
            ctx.Response.StatusCode = 500;
            return;
        }

        await mapper(ctx, response);
    }
    catch (BindingValidationException ex)
    {
        ctx.Response.StatusCode = ex.StatusCode;
        if (ex.Errors.Count > 0)
        {
            await ParameterValidator.WriteValidationError(ctx, ex.Errors);
        }
    }
    catch (TaskCanceledException)
    {
        ctx.Response.StatusCode = 504;
    }
    catch (AskTimeoutException)
    {
        ctx.Response.StatusCode = 504;
    }
    catch
    {
        ctx.Response.StatusCode = 500;
    }
}
```

- [ ] **Step 2: Update ExecuteTell with pluggable response handler**

Replace `ExecuteTell`:

```csharp
private async Task ExecuteTell(TurboHttpContext ctx, CancellationToken cancellationToken)
{
    try
    {
        var actorRef = await ResolveActor(ctx.RequestServices, cancellationToken);
        var message = await _methodConfig.MessageFactory(ctx, ctx.RequestServices);
        actorRef.Tell(message);

        if (_methodConfig.TellResponseHandler is not null)
        {
            await _methodConfig.TellResponseHandler(ctx);
        }
        else
        {
            ctx.Response.StatusCode = 202;
        }
    }
    catch (BindingValidationException ex)
    {
        ctx.Response.StatusCode = ex.StatusCode;
        if (ex.Errors.Count > 0)
        {
            await ParameterValidator.WriteValidationError(ctx, ex.Errors);
        }
    }
    catch
    {
        ctx.Response.StatusCode = 503;
    }
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build --configuration Release src/TurboHTTP.slnx`
Expected: Build succeeds with 0 errors.

- [ ] **Step 4: Run all routing tests**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Routing.TurboEntityBuilderSpec"`
Expected: All 12 tests pass.

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Routing.EntityResponseMapperCollectionSpec"`
Expected: All 5 tests pass.

- [ ] **Step 5: Commit**

```
feat(routing): update EntityDispatcher with two-tier mapper lookup and pluggable tell handler
```

---

### Task 7: Full Verification

**Files:** None (verification only)

- [ ] **Step 1: Run all routing tests together**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -namespace "TurboHTTP.Tests.Routing"`
Expected: All routing tests pass — TurboEntityBuilderSpec, TurboEntityAskBuilderSpec, TurboEntityTellBuilderSpec, EntityResponseMapperCollectionSpec, EntityDelegateBindingSpec.

- [ ] **Step 2: Run full test suite**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj`
Expected: All tests pass. No regressions.

- [ ] **Step 3: Build Release**

Run: `dotnet build --configuration Release src/TurboHTTP.slnx`
Expected: 0 errors, 0 warnings (except the expected CS0618 obsolete warning in the compat test).

- [ ] **Step 4: Run Roslyn diagnostics on changed files**

Use `mcp__cwm-roslyn-navigator__get_diagnostics` on the solution to verify zero compile-time issues.

- [ ] **Step 5: Commit (if any fixes needed)**

```
chore: fix verification findings
```
