using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Servus.Akka.Diagnostics;

/// <summary>
/// Static API for zero-cost developer tracing. When no listener is configured,
/// trace calls are no-ops (single null-check + inlined branch).
/// <see cref="Configure"/> is called once at startup before any worker threads exist,
/// so the thread-creation happens-before guarantees visibility without barriers.
/// </summary>
internal static class ServusTrace
{
    private static TraceConfig? _config;

    /// <summary>
    /// Enables tracing with the specified listener, category filter, and minimum level.
    /// Must be called before the Akka actor system starts — thread creation provides
    /// happens-before visibility to all worker threads.
    /// </summary>
    public static void Configure(
        IServusTraceListener listener,
        ServusTraceCategory categories = ServusTraceCategory.All,
        ServusTraceLevel minimumLevel = ServusTraceLevel.Trace)
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
    internal static bool ShouldTrace(ServusTraceCategory category, ServusTraceLevel level)
    {
        var cfg = _config;
        return cfg is not null && cfg.Listener.IsEnabled(level, category)
                               && (cfg.EnabledCategories & category) != 0
                               && level >= cfg.MinimumLevel;
    }

    internal static void WriteEvent(in ServusTraceEvent evt)
    {
        _config?.Listener.Write(in evt);
    }

    private sealed record TraceConfig(
        IServusTraceListener Listener,
        ServusTraceCategory EnabledCategories,
        ServusTraceLevel MinimumLevel);

    /// <summary>Trace category for TCP/QUIC connection lifecycle events.</summary>
    public static class Connection
    {
        private const ServusTraceCategory Category = ServusTraceCategory.Connection;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(object source, string message)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Trace)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Trace, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Trace(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Trace)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Trace, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(object source, string message)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Debug)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Debug, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Debug(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Debug)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Debug, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(object source, string message)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Info)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Info, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Info(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Info)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Info, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warning(object source, string message)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Warning)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Warning(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Warning)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(object source, string message)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Error)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Error, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Error(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Error)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Error, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }
    }

    /// <summary>Trace category for DNS resolution events.</summary>
    public static class Dns
    {
        private const ServusTraceCategory Category = ServusTraceCategory.Dns;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(object source, string message)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Trace)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Trace, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Trace(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Trace)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Trace, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(object source, string message)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Debug)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Debug, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Debug(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Debug)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Debug, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(object source, string message)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Info)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Info, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Info(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Info)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Info, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warning(object source, string message)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Warning)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Warning(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Warning)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(object source, string message)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Error)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Error, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Error(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Error)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Error, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }
    }

    /// <summary>Trace category for TLS handshake events.</summary>
    public static class Tls
    {
        private const ServusTraceCategory Category = ServusTraceCategory.Tls;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(object source, string message)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Trace)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Trace, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Trace(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Trace)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Trace, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(object source, string message)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Debug)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Debug, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Debug(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Debug)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Debug, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(object source, string message)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Info)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Info, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Info(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Info)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Info, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warning(object source, string message)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Warning)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Warning(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Warning)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(object source, string message)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Error)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Error, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Error(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Error)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Error, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }
    }

    /// <summary>Trace category for connection pool lifecycle events.</summary>
    public static class Pool
    {
        private const ServusTraceCategory Category = ServusTraceCategory.Pool;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(object source, string message)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Trace)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Trace, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Trace(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Trace)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Trace, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(object source, string message)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Debug)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Debug, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Debug(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Debug)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Debug, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(object source, string message)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Info)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Info, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Info(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Info)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Info, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warning(object source, string message)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Warning)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Warning(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Warning)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Warning, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(object source, string message)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Error)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Error, Category,
                source.GetType().Name, source.GetHashCode(), message));
        }

        public static void Error(object source, string message, params object?[] args)
        {
            if (!ShouldTrace(Category, ServusTraceLevel.Error)) return;
            WriteEvent(new ServusTraceEvent(Stopwatch.GetTimestamp(), ServusTraceLevel.Error, Category,
                source.GetType().Name, source.GetHashCode(), message, args));
        }
    }
}
