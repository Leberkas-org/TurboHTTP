namespace TurboHTTP.Context.Features;

public interface ITurboRequestIdentifierFeature
{
    string TraceIdentifier { get; set; }
}
