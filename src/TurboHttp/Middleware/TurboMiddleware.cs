using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TurboHttp.Middleware;

public abstract class TurboMiddleware
{
    public virtual ValueTask<HttpRequestMessage> ProcessRequestAsync(HttpRequestMessage request, CancellationToken ct)
        => ValueTask.FromResult(request);

    public virtual ValueTask<HttpResponseMessage> ProcessResponseAsync(HttpRequestMessage original, HttpResponseMessage response, CancellationToken ct)
        => ValueTask.FromResult(response);
}
