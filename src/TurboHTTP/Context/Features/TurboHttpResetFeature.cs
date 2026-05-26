using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;

namespace TurboHTTP.Context.Features;

internal sealed class TurboHttpResetFeature : IHttpResetFeature, ITurboResetFeature
{
    private readonly Action<int> _resetCallback;

    public TurboHttpResetFeature(Action<int> resetCallback)
    {
        _resetCallback = resetCallback;
    }

    public void Reset(int errorCode) => _resetCallback(errorCode);
}
