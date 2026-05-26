using TurboHTTP.Context;

namespace TurboHTTP.Context.Features;

public interface ITurboRequestFeature
{
    string Protocol { get; set; }
    string Scheme { get; set; }
    string Method { get; set; }
    string PathBase { get; set; }
    string Path { get; set; }
    string QueryString { get; set; }
    string RawTarget { get; set; }
    ITurboHeaderDictionary Headers { get; }
    Stream Body { get; set; }
}
