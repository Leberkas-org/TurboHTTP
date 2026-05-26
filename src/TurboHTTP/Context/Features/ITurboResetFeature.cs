namespace TurboHTTP.Context.Features;

public interface ITurboResetFeature
{
    void Reset(int errorCode);
}
