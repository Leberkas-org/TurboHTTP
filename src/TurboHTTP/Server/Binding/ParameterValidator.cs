using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace TurboHTTP.Server.Binding;

internal static class ParameterValidator
{
    public static ValidationResult ValidateObject(object value, string parameterName)
    {
        var context = new ValidationContext(value);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        if (Validator.TryValidateObject(value, context, results, validateAllProperties: true))
        {
            return ValidationResult.Valid;
        }

        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in results)
        {
            var memberNames = r.MemberNames.Any() ? r.MemberNames : [parameterName];
            foreach (var member in memberNames)
            {
                if (!errors.TryGetValue(member, out var list))
                {
                    list = [];
                    errors[member] = list;
                }

                list.Add(r.ErrorMessage ?? "Validation failed.");
            }
        }

        return new ValidationResult(false, errors);
    }

    public static bool HasValidationAttributes(Type type)
    {
        foreach (var prop in type.GetProperties())
        {
            if (prop.GetCustomAttributes(typeof(ValidationAttribute), true).Length > 0)
            {
                return true;
            }
        }

        foreach (var param in type.GetConstructors()
                     .Where(c => c.IsPublic)
                     .SelectMany(c => c.GetParameters()))
        {
            if (param.GetCustomAttributes(typeof(ValidationAttribute), true).Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    public static async Task WriteValidationError(TurboHttpContext context, Dictionary<string, List<string>> errors)
    {
        context.Response.StatusCode = 400;
        context.Response.ContentType = "application/problem+json";

        var problemDetails = new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            title = "Validation Failed",
            status = 400,
            errors
        };

        var json = JsonSerializer.Serialize(problemDetails);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        await context.Response.Body.WriteAsync(bytes);
    }

    internal sealed class ValidationResult(bool isValid, Dictionary<string, List<string>> errors)
    {
        public static readonly ValidationResult Valid = new(true, new Dictionary<string, List<string>>());

        public bool IsValid { get; } = isValid;
        public Dictionary<string, List<string>> Errors { get; } = errors;
    }
}
