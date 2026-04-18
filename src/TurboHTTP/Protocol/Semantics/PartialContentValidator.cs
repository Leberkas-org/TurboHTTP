using System.Net;

namespace TurboHTTP.Protocol.Semantics;

/// <summary>
/// RFC 9110 §15.3.7 — validates 206 Partial Content responses.
/// <list type="bullet">
///   <item>A 206 response MUST contain a Content-Range header field (single part)
///         or a multipart/byteranges Content-Type (multiple parts).</item>
///   <item>Non-206 responses are not subject to this validation.</item>
/// </list>
/// </summary>
internal static class PartialContentValidator
{
    /// <summary>
    /// Result of validating a 206 Partial Content response.
    /// </summary>
    internal sealed class ValidationResult
    {
        /// <summary>Whether the response is a valid 206 Partial Content response.</summary>
        public bool IsValid { get; init; }

        /// <summary>Whether the response uses multipart/byteranges content type.</summary>
        public bool IsMultipartByteRanges { get; init; }

        /// <summary>Error message when <see cref="IsValid"/> is false, otherwise null.</summary>
        public string? ErrorMessage { get; init; }

        /// <summary>Whether validation was skipped (non-206 response).</summary>
        public bool Skipped { get; private init; }

        internal static readonly ValidationResult SkippedResult = new()
        {
            IsValid = true,
            Skipped = true
        };
    }

    /// <summary>
    /// Validates the given <paramref name="response"/>.
    /// Returns a <see cref="ValidationResult"/> describing validity.
    /// Non-206 responses are skipped (always valid).
    /// </summary>
    public static ValidationResult Validate(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            return ValidationResult.SkippedResult;
        }

        // Check for multipart/byteranges content type (RFC 9110 §15.3.7)
        var contentType = response.Content.Headers.ContentType;
        if (contentType is not null &&
            string.Equals(contentType.MediaType, "multipart/byteranges", StringComparison.OrdinalIgnoreCase))
        {
            return new ValidationResult
            {
                IsValid = true,
                IsMultipartByteRanges = true
            };
        }

        // Single-part 206 MUST have Content-Range header
        if (!response.Content.Headers.Contains("Content-Range"))
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = "RFC 9110 §15.3.7: A 206 response MUST contain a Content-Range header field or use multipart/byteranges content type."
            };
        }

        return new ValidationResult
        {
            IsValid = true
        };
    }
}
