using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Server.Context.Features;

internal sealed class TurboHttpResetFeature : IHttpResetFeature
{
    private readonly Action<int> _resetCallback;

    public TurboHttpResetFeature(Action<int> resetCallback)
    {
        _resetCallback = resetCallback;
    }

    public void Reset(int errorCode) => _resetCallback(errorCode);
}
