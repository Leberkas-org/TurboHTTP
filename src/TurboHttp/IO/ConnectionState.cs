using System;
using Akka.Actor;

namespace TurboHttp.IO;

internal sealed class ConnectionState
{
    public IActorRef Actor { get; }
    public bool Active { get; private set; } = true;
    public bool Idle { get; private set; } = true;
    public int PendingRequests { get; private set; }
    public DateTime LastActivity { get; private set; } = DateTime.UtcNow;

    public ConnectionState(IActorRef actor)
    {
        Actor = actor;
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
}
