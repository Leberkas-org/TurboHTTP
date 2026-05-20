# Entity Routing Fluent API — IsAsk/IsTell Design

## Summary

Redesign `MapTurboEntity` method builders to use callback-based `IsAsk()`/`IsTell()` pattern, add per-endpoint response handlers, and support ASP.NET `IResult`/`TypedResults` via `Produces<T>()`.

## Motivation

The current API uses `.AcceptedResponse()` to switch to Tell pattern — indirect and non-discoverable. Response mappers are entity-wide only (`MapResponse<T>`), with no per-endpoint control. There is no `IResult`/`TypedResults` integration.

## Design

### Public API

#### `TurboEntityMethodBuilder` (modified)

```csharp
public sealed class TurboEntityMethodBuilder
{
    // New: callback-based Ask/Tell configuration
    public void IsAsk(Action<TurboEntityAskBuilder> configure);
    public void IsTell(Action<TurboEntityTellBuilder>? configure = null);

    // Existing: bare usage (backwards compat — Ask with entity-level mappers)
    public TurboEntityMethodBuilder WithTimeout(TimeSpan timeout);

    // Deprecated
    [Obsolete("Use .IsTell() instead")]
    public TurboEntityMethodBuilder AcceptedResponse();
}
```

- `IsAsk(configure)` — non-nullable callback, must register at least one `Response<T>` or `Produces<T>`. Throws `InvalidOperationException` if the callback adds no handlers.
- `IsTell(configure?)` — nullable callback, defaults to 202 Accepted when null/omitted.
- Bare usage (`OnGet(factory)` without `IsAsk`/`IsTell`) — Ask with entity-level `MapResponse<T>` fallback, same as current behavior.
- `IsAsk` and `IsTell` are mutually exclusive. If both are called on the same method builder (via separate statements), last call wins. The `void` return type naturally prevents fluent chaining of both.

#### `TurboEntityAskBuilder` (new)

```csharp
public sealed class TurboEntityAskBuilder
{
    // Direct context writing
    public TurboEntityAskBuilder Response<TResponse>(
        Func<TurboHttpContext, TResponse, Task> handler);

    // IResult-based (TypedResults/Results)
    public TurboEntityAskBuilder Produces<TResponse>(
        Func<TurboHttpContext, TResponse, IResult> handler);

    public TurboEntityAskBuilder WithTimeout(TimeSpan timeout);
}
```

- `Response<T>` — handler writes directly to `TurboHttpContext` (status code, headers, body).
- `Produces<T>` — handler returns `IResult`, executed via `IResult.ExecuteAsync(ctx)`. Works because `TurboHttpContext : HttpContext`.
- Both register into the same internal `EntityResponseMapperCollection`. `Produces<T>` wraps the IResult execution into `Func<TurboHttpContext, object, Task>`.
- Multiple handlers chainable (separate statements or fluent).

#### `TurboEntityTellBuilder` (new)

```csharp
public sealed class TurboEntityTellBuilder
{
    // Simple status code
    public TurboEntityTellBuilder Response(int statusCode);

    // Status code + write headers/body
    public TurboEntityTellBuilder Response(int statusCode,
        Func<TurboHttpContext, Task> writer);

    // IResult-based
    public TurboEntityTellBuilder Produces(
        Func<TurboHttpContext, IResult> factory);
}
```

- `Response(int)` — sets status code only.
- `Response(int, writer)` — sets status code, then calls writer for headers/body.
- `Produces(factory)` — delegates entirely to `IResult.ExecuteAsync(ctx)`.
- All three compile into a single `Func<TurboHttpContext, Task>?` internally. Last call wins.

### Response Resolution Order (Ask)

1. Per-endpoint handlers (from `IsAsk` callback) — exact type match
2. Per-endpoint handlers — assignable match (inheritance/interfaces)
3. Entity-level `MapResponse<T>` — exact type match
4. Entity-level `MapResponse<T>` — assignable match
5. No mapper found → 500 Internal Server Error

### Internal Changes

#### `EntityMethodConfig` (modified)

```csharp
internal sealed record EntityMethodConfig(
    Func<TurboHttpContext, IServiceProvider, ValueTask<object>> MessageFactory,
    bool IsTell,
    TimeSpan? TimeoutOverride,
    EntityResponseMapperCollection? EndpointMappers,
    Func<TurboHttpContext, Task>? TellResponseHandler);
```

- `EndpointMappers` — null when bare usage or Tell. Populated by `IsAsk` callback.
- `TellResponseHandler` — null means default 202. Populated by `IsTell` callback.

#### `TurboEntityMethodBuilder` internals

Sub-builders hold their own state. Method builder reads after callback executes:

```csharp
public void IsAsk(Action<TurboEntityAskBuilder> configure)
{
    _isTell = false;
    var builder = new TurboEntityAskBuilder();
    configure(builder);
    if (builder.Mappers.Count == 0)
        throw new InvalidOperationException(
            "IsAsk requires at least one Response<T> or Produces<T> handler.");
    _endpointMappers = builder.Mappers;
    _timeoutOverride = builder.TimeoutOverride ?? _timeoutOverride;
}

public void IsTell(Action<TurboEntityTellBuilder>? configure = null)
{
    _isTell = true;
    if (configure is not null)
    {
        var builder = new TurboEntityTellBuilder();
        configure(builder);
        _tellResponseHandler = builder.ResponseHandler;
    }
}
```

#### `Produces<T>` wrapping

`Produces<T>` internally wraps IResult into the same delegate format as `Response<T>`:

```csharp
public TurboEntityAskBuilder Produces<TResponse>(
    Func<TurboHttpContext, TResponse, IResult> handler)
{
    Mappers.Add<TResponse>(async (ctx, resp) =>
        await handler(ctx, resp).ExecuteAsync(ctx));
    return this;
}
```

The `EntityResponseMapperCollection` is unchanged — it stores `Func<TurboHttpContext, object, Task>` regardless of whether the handler was registered via `Response<T>` or `Produces<T>`.

#### `EntityDispatcher` changes

**Ask path** — two-tier mapper lookup:

```csharp
var mapper = _methodConfig.EndpointMappers?.FindMapper(response.GetType())
          ?? _responseMappers.FindMapper(response.GetType());
```

**Tell path** — pluggable response handler:

```csharp
actorRef.Tell(message);
if (_methodConfig.TellResponseHandler is not null)
    await _methodConfig.TellResponseHandler(ctx);
else
    ctx.Response.StatusCode = 202;
```

### IResult Bridge

`TurboHttpContext` inherits from `HttpContext`, so `IResult.ExecuteAsync(ctx)` works directly with no adapter. This is the same pattern already used by `TurboStreamResults` (EventStreamResult, AkkaStreamResult).

### Backwards Compatibility

| Usage | Before | After |
|-------|--------|-------|
| `OnGet(factory)` bare | Ask, entity-level mappers | Unchanged |
| `.AcceptedResponse()` | Tell, 202 | `[Obsolete]`, internally = `IsTell()` |
| `MapResponse<T>()` on entity builder | Entity-wide fallback | Unchanged, still serves as fallback |
| `WithTimeout()` on method builder | Per-method timeout | Unchanged |

No breaking changes. `AcceptedResponse()` is deprecated but continues to work.

## Usage Examples

```csharp
app.MapTurboEntity("/orders/{id}", entity =>
{
    entity.UseActorRef<OrderActor>();
    entity.WithTimeout(TimeSpan.FromSeconds(30));

    // Entity-level fallback mapper (unchanged)
    entity.MapResponse<ErrorResponse>((ctx, err) =>
    {
        ctx.Response.StatusCode = 500;
        return Task.CompletedTask;
    });

    // Ask — per-endpoint with IResult
    entity.OnGet((int id) => new GetOrder(id))
        .IsAsk(ask =>
        {
            ask.Produces<OrderDto>((ctx, order) => TypedResults.Ok(order));
            ask.Produces<NotFoundResult>((ctx, _) => TypedResults.NotFound());
            ask.WithTimeout(TimeSpan.FromSeconds(10));
        });

    // Ask — per-endpoint with direct writing
    entity.OnPut((int id, UpdateOrderDto dto) => new UpdateOrder(id, dto))
        .IsAsk(ask =>
        {
            ask.Response<OrderDto>(async (ctx, order) =>
                await ctx.Response.WriteAsJsonAsync(order));
        });

    // Tell — default 202
    entity.OnDelete((int id) => new DeleteOrder(id))
        .IsTell();

    // Tell — custom status code
    entity.OnPost((int id, CreateOrderDto dto) => new CreateOrder(id, dto))
        .IsTell(tell =>
        {
            tell.Response(StatusCodes.Status204NoContent);
        });

    // Tell — with response body/headers
    entity.OnPost((int id, StartJobDto dto) => new StartJob(id, dto))
        .IsTell(tell =>
        {
            tell.Response(202, async ctx =>
            {
                ctx.Response.Headers.Location = $"/jobs/{ctx.RouteValues["id"]}";
            });
        });

    // Tell — IResult
    entity.OnPost((int id, FireDto dto) => new Fire(id, dto))
        .IsTell(tell =>
        {
            tell.Produces(ctx => TypedResults.Accepted());
        });

    // Bare — backwards compat Ask with entity-level mappers
    entity.OnPatch((int id, PatchDto dto) => new PatchOrder(id, dto));
});
```

## Files Changed

| File | Change |
|------|--------|
| `Server/TurboEntityMethodBuilder.cs` | Add `IsAsk()`, `IsTell()`, internal state fields, deprecate `AcceptedResponse()` |
| `Server/TurboEntityAskBuilder.cs` | **New** — `Response<T>`, `Produces<T>`, `WithTimeout` |
| `Server/TurboEntityTellBuilder.cs` | **New** — `Response(int)`, `Response(int, writer)`, `Produces(factory)` |
| `Routing/EntityMethodConfig.cs` | Add `EndpointMappers`, `TellResponseHandler` parameters |
| `Routing/EntityDispatcher.cs` | Two-tier mapper lookup in Ask, pluggable handler in Tell |
| Tests: `TurboEntityBuilderSpec.cs` | New tests covering IsAsk/IsTell, Produces, resolution order, validation |

## Test Plan

- **IsAsk with Response<T>**: register typed handler, verify it receives the correct response type
- **IsAsk with Produces<T>**: register IResult handler, verify `ExecuteAsync` is called on TurboHttpContext
- **IsAsk with multiple handlers**: register 2+ types, verify exact match takes precedence
- **IsAsk validation**: empty callback throws `InvalidOperationException`
- **IsAsk + entity-level fallback**: per-endpoint handler matched first, entity-level used for unmapped types
- **IsAsk with WithTimeout**: verify per-endpoint timeout overrides entity-level
- **IsTell default**: no callback → 202 Accepted
- **IsTell with Response(int)**: custom status code
- **IsTell with Response(int, writer)**: status code + body/headers written
- **IsTell with Produces**: IResult executed on TurboHttpContext
- **Bare usage**: no IsAsk/IsTell → Ask with entity-level mappers (backwards compat)
- **AcceptedResponse deprecated**: still works, equivalent to IsTell()
