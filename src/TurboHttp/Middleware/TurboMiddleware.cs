using System.Net.Http;

namespace TurboHttp;

public abstract class TurboHandler
{
    public virtual HttpRequestMessage ProcessRequest(HttpRequestMessage request)
        => request;

    public virtual HttpResponseMessage ProcessResponse(HttpRequestMessage original, HttpResponseMessage response)
        => response;
}
