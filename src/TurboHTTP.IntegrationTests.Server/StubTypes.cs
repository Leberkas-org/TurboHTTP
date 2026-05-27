using System.IO.Pipelines;
using Akka;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http;

namespace TurboHTTP.Server;

/// <summary>
/// Temporary stub for deleted app-framework layer.
/// These types were part of the v1.3.0 middleware/streaming pipeline that has been removed.
/// Integration tests that use these are awaiting migration to the new RequestContext-based pipeline.
/// </summary>
[Obsolete("App-framework layer has been deleted")]
public sealed class TurboPipelineBuilder
{
    public void Use(Func<HttpContext, Func<HttpContext, Task>, Task> middleware) { }
    public void Map(string pattern, Action<TurboPipelineBuilder> configure) { }
}

/// <summary>
/// Temporary stub for deleted app-framework layer.
/// These types were part of the v1.3.0 middleware/streaming pipeline that has been removed.
/// Integration tests that use these are awaiting migration to the new RequestContext-based pipeline.
/// </summary>
[Obsolete("App-framework layer has been deleted")]
public static class TurboStreamResults
{
    public static IResult Stream(Source<ReadOnlyMemory<byte>, NotUsed> source, string? contentType = null)
        => Results.Ok("");

    public static IResult Stream(Func<PipeWriter, CancellationToken, ValueTask> handler)
        => Results.Ok("");

    public static IResult EventStream(Source<string, NotUsed> source)
        => Results.Ok("");
}
