using Microsoft.AspNetCore.Http;

namespace TurboHTTP.Context;

internal sealed class TurboFormFile : IFormFile
{
    private readonly byte[] _content;

    public TurboFormFile(string name, string fileName, string contentType, byte[] content)
    {
        Name = name;
        FileName = fileName;
        ContentType = contentType;
        _content = content;
        Length = content.Length;
        Headers = new HeaderDictionary();
    }

    public string ContentDisposition => string.Concat("form-data; name=\"", Name, "\"; filename=\"", FileName, "\"");
    public string ContentType { get; }
    public string FileName { get; }
    public IHeaderDictionary Headers { get; }
    public long Length { get; }
    public string Name { get; }

    public void CopyTo(Stream target) => target.Write(_content, 0, _content.Length);
    public async Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
        => await target.WriteAsync(_content, cancellationToken);
    public Stream OpenReadStream() => new MemoryStream(_content, writable: false);
}
