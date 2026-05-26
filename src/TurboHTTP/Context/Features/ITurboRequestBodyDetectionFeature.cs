namespace TurboHTTP.Context.Features;

public interface ITurboRequestBodyDetectionFeature
{
    bool CanHaveBody { get; }
}
