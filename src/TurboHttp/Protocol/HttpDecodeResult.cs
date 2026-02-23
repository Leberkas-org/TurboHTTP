namespace TurboHttp.Protocol;

public readonly struct HttpDecodeResult
{
    public bool Success { get; }
    public HttpDecodeError? Error { get; }

    private HttpDecodeResult(bool success, HttpDecodeError? error)
    {
        Success = success;
        Error = error;
    }

    public static HttpDecodeResult Ok() => new(true, null);
    public static HttpDecodeResult Incomplete() => new(false, HttpDecodeError.NeedMoreData);
    public static HttpDecodeResult Fail(HttpDecodeError err) => new(false, err);
}