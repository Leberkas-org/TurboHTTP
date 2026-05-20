using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Protocol;

internal interface IProtocolSwitchCapable
{
    void RequestProtocolSwitch(
        Func<IServerStageOperations, IServerStateMachine> newSmFactory);
}
