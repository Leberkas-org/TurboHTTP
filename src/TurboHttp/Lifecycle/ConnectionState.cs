using System;
using Akka.Actor;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;

namespace TurboHttp.Lifecycle;

internal sealed class ConnectionState
{
    public IActorRef Actor { get; }
    public bool Active { get; private set; } = true;
    public bool Idle { get; private set; } = true;
    public int PendingRequests { get; private set; }
    public DateTime LastActivity { get; private set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this connection can be reused for subsequent requests.
    /// Set to false when a Connection: close header is received or server signals close.
    /// </summary>
    public bool Reusable { get; private set; } = true;

    /// <summary>
    /// The connection handle providing direct Channel I/O access to the TCP connection.
    /// Set when the connection becomes ready via <see cref="SetHandle"/>.
    /// </summary>
    public ConnectionHandle? Handle { get; private set; }

    /// <summary>
    /// The HTTP version this connection is operating under.
    /// Computed from the <see cref="Handle"/>'s <see cref="RequestEndpoint.Version"/>;
    /// defaults to HTTP/1.1 when no handle is attached.
    /// </summary>
    public Version HttpVersion => Handle?.Key.Version ?? System.Net.HttpVersion.Version11;

    /// <summary>
    /// Maximum concurrent streams allowed on this connection.
    /// Returns version-dependent defaults:
    /// <list type="bullet">
    ///   <item>HTTP/1.0: 1 (no pipelining)</item>
    ///   <item>HTTP/1.1: 6 (pipeline depth default)</item>
    ///   <item>HTTP/2+: live value from <see cref="ConnectionHandle.MaxConcurrentStreams"/>, or 100 if no handle</item>
    /// </list>
    /// </summary>
    public int MaxConcurrentStreams
    {
        get
        {
            var version = HttpVersion;

            if (version.Major == 1 && version.Minor == 0)
            {
                return 1;
            }

            if (version.Major == 1)
            {
                return 6;
            }

            // HTTP/2+
            return Handle?.MaxConcurrentStreams ?? 100;
        }
    }

    /// <summary>
    /// Whether this connection can accept another request.
    /// </summary>
    public bool HasAvailableSlot => Active && Reusable && Handle is not null && PendingRequests < MaxConcurrentStreams;

    public ConnectionState(IActorRef actor)
    {
        Actor = actor;
    }

    /// <summary>
    /// Associates a <see cref="ConnectionHandle"/> with this connection state.
    /// Updates <see cref="LastActivity"/> to the current time.
    /// </summary>
    public void SetHandle(ConnectionHandle handle)
    {
        Handle = handle;
        LastActivity = DateTime.UtcNow;
    }

    public void MarkBusy()
    {
        Idle = false;
        PendingRequests++;
        LastActivity = DateTime.UtcNow;
    }

    public void MarkIdle()
    {
        PendingRequests--;

        if (PendingRequests == 0)
        {
            Idle = true;
        }

        LastActivity = DateTime.UtcNow;
    }

    public void MarkDead()
    {
        Active = false;
    }

    /// <summary>
    /// Marks this connection as non-reusable (e.g., after receiving Connection: close).
    /// </summary>
    public void MarkNoReuse()
    {
        Reusable = false;
    }
}
