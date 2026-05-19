# Validation

TurboHTTP Server automatically validates handler parameters using standard `System.ComponentModel.DataAnnotations` attributes. The `ParameterValidator` inspects validation attributes on bound parameters and writes a 400 Bad Request response with structured error details if validation fails.

## Basic Usage

Decorate your request types with validation attributes. The server validates all parameters after binding:

```csharp
public record CreateUserRequest(
    [Required] [StringLength(100)] string Name,
    [Required] [EmailAddress] string Email,
    [Range(18, 150)] int Age);

public class UserHandler
{
    [Post("/users")]
    public async Task<IResult> CreateUser(CreateUserRequest request)
    {
        // Request is guaranteed to be valid at this point
        return Results.Created($"/users/{request.Id}", request);
    }
}
```

When validation fails, the server automatically returns a 400 Bad Request with error details:

```json
{
  "errors": {
    "Name": ["The Name field is required."],
    "Email": ["The Email field is not a valid e-mail address."],
    "Age": ["The field Age must be between 18 and 150."]
  }
}
```

## Supported Attributes

| Attribute | Behavior | Example |
|-----------|----------|---------|
| `[Required]` | Field must have a value (non-null, non-empty for strings) | `[Required] string Name` |
| `[StringLength(max)]` | String length must not exceed max | `[StringLength(100)]` |
| `[StringLength(min, max)]` | String length must be between min and max | `[StringLength(3, 50)]` |
| `[Range(min, max)]` | Numeric value must be between min and max | `[Range(0, 100)]` |
| `[RegularExpression(pattern)]` | Value must match the regex pattern | `[RegularExpression(@"^\d{5}$")]` |
| `[EmailAddress]` | Value must be a valid email format | `[EmailAddress]` |
| `[Phone]` | Value must be a valid phone number format | `[Phone]` |
| `[Url]` | Value must be a valid URL | `[Url]` |
| `[MinLength(length)]` | Collection or string must have at least length items/characters | `[MinLength(1)]` |
| `[MaxLength(length)]` | Collection or string must have at most length items/characters | `[MaxLength(50)]` |
| `[Compare(property)]` | Value must equal another property (useful for password confirmation) | `[Compare(nameof(Password))]` |

## Error Response Format

When validation fails, the server returns HTTP 400 with a JSON body containing an `errors` object. Each property name maps to an array of error messages:

```json
{
  "errors": {
    "Name": [
      "The Name field is required."
    ],
    "Email": [
      "The Email field is not a valid e-mail address."
    ],
    "Age": [
      "The field Age must be between 18 and 150."
    ]
  }
}
```

Multiple validation failures on a single field are included in the same array:

```json
{
  "errors": {
    "Password": [
      "The Password field is required.",
      "The field Password must be a string with a minimum length of 8 and maximum length of 100."
    ]
  }
}
```

## Validation on Composite Types

When using `[AsParameters]` to bind multiple types, validation runs recursively on all nested properties:

```csharp
public record PaginationParams(
    [Range(1, int.MaxValue)] int Page,
    [Range(1, 100)] int PageSize);

public record SearchRequest(
    [Required] [StringLength(200)] string Query,
    [AsParameters] PaginationParams Pagination);

public class SearchHandler
{
    [Get("/search")]
    public async Task<IResult> Search(SearchRequest request)
    {
        // Both SearchRequest and PaginationParams are validated
        return Results.Ok();
    }
}
```

## Custom Validation

For complex validation logic that can't be expressed with attributes alone, implement `IValidatableObject` on your request type:

```csharp
public record UpdateProductRequest(
    string Name,
    decimal Price,
    decimal DiscountedPrice) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (DiscountedPrice > Price)
        {
            yield return new ValidationResult(
                "Discounted price cannot be greater than regular price.",
                new[] { nameof(DiscountedPrice) });
        }

        if (Price <= 0)
        {
            yield return new ValidationResult(
                "Price must be greater than zero.",
                new[] { nameof(Price) });
        }
    }
}
```

The custom validation messages are merged into the error response alongside attribute-based validation errors.

::: tip
Validation runs automatically after binding completes. There is no need to explicitly call a validation method in your handler — if the handler method executes, validation has already succeeded.
:::

::: warning
If a parameter fails to bind (e.g., invalid type conversion), binding errors take precedence and validation is skipped. Always ensure your parameter types can be bound before adding validation attributes.
:::
