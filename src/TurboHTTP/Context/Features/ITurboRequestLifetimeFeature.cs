namespace TurboHTTP.Context.Features;

public interface ITurboRequestLifetimeFeature
{
    CancellationToken RequestAborted { get; set; }
    void Abort();
}
