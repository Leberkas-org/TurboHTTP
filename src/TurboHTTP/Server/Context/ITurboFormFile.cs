namespace TurboHTTP.Server.Context;

public interface ITurboFormFile
{
    string Name { get; }
    string FileName { get; }
    string ContentType { get; }
    long Length { get; }
    Stream OpenReadStream();
    void CopyTo(Stream target);
    Task CopyToAsync(Stream target, CancellationToken cancellationToken = default);
}
