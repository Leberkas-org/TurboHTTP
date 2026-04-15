using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace TurboHTTP.Diagnostics;

/// <summary>
/// Static API for zero-cost developer tracing. When no listener is configured,
/// trace calls are no-ops (single null-check + inlined branch).
/// <see cref="Configure"/> is called once at startup before any worker threads exist,
/// so the thread-creation happens-before guarantees visibility without barriers.
/// </summary>
public static class TurboTrace
{
    private static TraceConfig? _config;

    /// <summary>
    /// Enables tracing with the specified listener, category filter, and minimum level.
    /// Must be called before the Akka actor system starts — thread creation provides
    /// happens-before visibility to all worker threads.
    /// </summary>
    public static void Configure(
        ITurboTraceListener listener,
        TurboTraceCategory categories = TurboTraceCategory.All,
        TurboTraceLevel minimumLevel = TurboTraceLevel.Trace)
    {
        _config = new TraceConfig(listener, categories, minimumLevel);
    }

    /// <summary>
    /// Disables tracing. All subsequent trace calls become no-ops.
    /// </summary>
    public static void Disable()
    {
        _config = null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool ShouldTrace(TurboTraceCategory category, TurboTraceLevel level)
    {
        var cfg = _config;
        return cfg is not null && cfg.Listener.IsEnabled(level, category)
                               && (cfg.EnabledCategories & category) != 0
                               && level >= cfg.MinimumLevel;
    }

    internal static void WriteEvent(in TraceEvent evt)
    {
        _config?.Listener.Write(in evt);
    }

    private sealed record TraceConfig(
        ITurboTraceListener Listener,
        TurboTraceCategory EnabledCategories,
        TurboTraceLevel MinimumLevel);

    /// <summary>Trace category for connection lifecycle events.</summary>
    public static class Connection
    {
        private const TurboTraceCategory Category = TurboTraceCategory.Connection;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Trace)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Trace, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Trace(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Trace)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Trace, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Debug)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Debug(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Debug)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Info)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Info, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Info(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Info)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Info, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warning(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Warning)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Warning(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Warning)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Error)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Error, Category, source.GetType().Name,
                source.GetHashCode(), message, 0, null, null, null));
        }

        public static void Error(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Error)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Error, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }
    }

    /// <summary>Trace category for protocol-level events (HTTP/1.1, HTTP/2, HTTP/3).</summary>
    public static class Protocol
    {
        private const TurboTraceCategory Category = TurboTraceCategory.Protocol;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Trace)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Trace, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Trace(object source, string message, object? args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Trace)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Trace, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Debug)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, Category, source.GetType().Name,
                source.GetHashCode(), message, 0, null, null, null));
        }

        public static void Debug(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Debug)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Info)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Info, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Info(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Info)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Info, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warning(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Warning)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Warning(object source, string message, object? args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Warning)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Error)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Error, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Error(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Error)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Error, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }
    }

    /// <summary>Trace category for request processing events.</summary>
    public static class Request
    {
        private const TurboTraceCategory Category = TurboTraceCategory.Request;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Trace)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Trace, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Trace(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Trace)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Trace, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Debug)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Debug(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Debug)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Info)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Info, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Info(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Info)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Info, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warning(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Warning)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Warning(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Warning)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Error)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Error, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Error(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Error)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Error, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }
    }

    /// <summary>Trace category for response processing events.</summary>
    public static class Response
    {
        private const TurboTraceCategory Category = TurboTraceCategory.Response;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Trace)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Trace, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Trace(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Trace)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Trace, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Debug)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Debug(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Debug)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Info)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Info, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Info(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Info)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Info, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warning(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Warning)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Warning(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Warning)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Error)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Error, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Error(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Error)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Error, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }
    }

    /// <summary>Trace category for cache events.</summary>
    public static class Cache
    {
        private const TurboTraceCategory Category = TurboTraceCategory.Cache;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Trace)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Trace, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Trace(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Trace)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Trace, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Debug)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Debug(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Debug)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Info)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Info, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Info(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Info)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Info, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warning(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Warning)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Warning(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Warning)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Error)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Error, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Error(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Error)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Error, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }
    }

    /// <summary>Trace category for redirect handling events.</summary>
    public static class Redirect
    {
        private const TurboTraceCategory Category = TurboTraceCategory.Redirect;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Trace)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Trace, Category, source.GetType().Name,
                source.GetHashCode(), message, 0, null, null, null));
        }

        public static void Trace(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Trace)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Trace, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Debug)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, Category, source.GetType().Name,
                source.GetHashCode(), message, 0, null, null, null));
        }

        public static void Debug(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Debug)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Info)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Info, Category, source.GetType().Name,
                source.GetHashCode(), message, 0, null, null, null));
        }

        public static void Info(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Info)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Info, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warning(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Warning)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message, 0, null, null, null));
        }

        public static void Warning(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Warning)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Error)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Error, Category, source.GetType().Name,
                source.GetHashCode(), message, 0, null, null, null));
        }

        public static void Error(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Error)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Error, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }
    }

    /// <summary>Trace category for retry logic events.</summary>
    public static class Retry
    {
        private const TurboTraceCategory Category = TurboTraceCategory.Retry;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Trace)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Trace, Category, source.GetType().Name,
                source.GetHashCode(), message, 0, null, null, null));
        }

        public static void Trace(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Trace)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Trace, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Debug)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, Category, source.GetType().Name,
                source.GetHashCode(), message, 0, null, null, null));
        }

        public static void Debug(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Debug)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Info)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Info, Category, source.GetType().Name,
                source.GetHashCode(), message, 0, null, null, null));
        }

        public static void Info(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Info)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Info, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warning(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Warning)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message, 0, null, null, null));
        }

        public static void Warning(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Warning)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Error)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Error, Category, source.GetType().Name,
                source.GetHashCode(), message, 0, null, null, null));
        }

        public static void Error(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Error)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Error, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }
    }

    /// <summary>Trace category for connection pool events.</summary>
    public static class Pool
    {
        private const TurboTraceCategory Category = TurboTraceCategory.Pool;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Trace)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Trace, Category, source.GetType().Name,
                source.GetHashCode(), message, 0, null, null, null));
        }

        public static void Trace(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Trace)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Trace, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Debug)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, Category, source.GetType().Name,
                source.GetHashCode(), message, 0, null, null, null));
        }

        public static void Debug(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Debug)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Info)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Info, Category, source.GetType().Name,
                source.GetHashCode(), message, 0, null, null, null));
        }

        public static void Info(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Info)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Info, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warning(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Warning)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message, 0, null, null, null));
        }

        public static void Warning(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Warning)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Error)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Error, Category, source.GetType().Name,
                source.GetHashCode(), message, 0, null, null, null));
        }

        public static void Error(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Error)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Error, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }
    }

    /// <summary>Trace category for transport-level events (TCP, QUIC).</summary>
    public static class Transport
    {
        private const TurboTraceCategory Category = TurboTraceCategory.Transport;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Trace)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Trace, Category, source.GetType().Name,
                source.GetHashCode(), message, 0, null, null, null));
        }

        public static void Trace(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Trace)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Trace, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Debug)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, Category, source.GetType().Name,
                source.GetHashCode(), message, 0, null, null, null));
        }

        public static void Debug(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Debug)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Info)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Info, Category, source.GetType().Name,
                source.GetHashCode(), message, 0, null, null, null));
        }

        public static void Info(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Info)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Info, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warning(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Warning)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message, 0, null, null, null));
        }

        public static void Warning(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Warning)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Error)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Error, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Error(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Error)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Error, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }
    }

    /// <summary>Trace category for stream multiplexing events (HTTP/2, HTTP/3).</summary>
    public static class Stream
    {
        private const TurboTraceCategory Category = TurboTraceCategory.Stream;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Trace)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Trace, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Trace(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Trace)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Trace, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Debug)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Debug(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Debug)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Info)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Info, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Info(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Info)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Info, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warning(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Warning)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Warning(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Warning)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(object source, string message)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Error)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Error, Category, source.GetType().Name,
                source.GetHashCode(), message));
        }

        public static void Error(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, TurboTraceLevel.Error)) return;
            WriteEvent(new TraceEvent(Stopwatch.GetTimestamp(), TurboTraceLevel.Error, Category, source.GetType().Name,
                source.GetHashCode(), message, args));
        }
    }
}