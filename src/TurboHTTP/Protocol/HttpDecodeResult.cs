namespace TurboHTTP.Protocol;

public readonly struct HttpDecodeResult
{
    public bool Success { get; }
    public HttpDecoderError? Error { get; }

    private HttpDecodeResult(bool success, HttpDecoderError? error)
    {
        Success = success;
        Error = error;
    }

    public static HttpDecodeResult Ok() => new(true, null);
    public static HttpDecodeResult Incomplete() => new(false, HttpDecoderError.NeedMoreData);
    public static HttpDecodeResult Fail(HttpDecoderError err) => new(false, err);
}