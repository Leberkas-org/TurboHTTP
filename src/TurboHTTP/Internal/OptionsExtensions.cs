using TurboHTTP.Streams;

namespace TurboHTTP.Internal;

internal static class OptionsExtensions
{
    public static Http1EngineOptions ToEngineOptions(this Http1Options options)
    {
        return new Http1EngineOptions(
            options.MaxPipelineDepth,
            options.MaxConnectionsPerServer,
            options.MaxReconnectAttempts,
            options.MaxBatchWeight,
            options.MaxResponseHeadersLength,
            options.MaxResponseDrainSize,
            options.ResponseDrainTimeout);
    }

    public static Http2EngineOptions ToEngineOptions(this Http2Options options)
    {
        return new Http2EngineOptions(
            options.MaxConnectionsPerServer,
            options.MaxConcurrentStreams,
            options.InitialConnectionWindowSize,
            options.InitialStreamWindowSize,
            options.MaxFrameSize,
            options.HeaderTableSize,
            options.MaxReconnectAttempts,
            options.MaxBatchWeight,
            options.KeepAlivePingDelay,
            options.KeepAlivePingTimeout,
            options.KeepAlivePingPolicy);
    }

    public static Http3EngineOptions ToEngineOptions(this Http3Options options)
    {
        return new Http3EngineOptions(
            options.MaxFieldSectionSize,
            options.QpackMaxTableCapacity,
            options.QpackBlockedStreams,
            options.IdleTimeout,
            options.MaxReconnectAttempts,
            options.AllowServerPush,
            options.AllowEarlyData,
            options.AllowConnectionMigration);
    }
}