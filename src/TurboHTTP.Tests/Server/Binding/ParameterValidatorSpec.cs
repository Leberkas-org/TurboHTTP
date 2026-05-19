using System.ComponentModel.DataAnnotations;
using TurboHTTP.Server.Binding;

namespace TurboHTTP.Tests.Server.Binding;

public sealed class ParameterValidatorSpec
{
    [Fact(Timeout = 5000)]
    public void Validate_should_pass_for_valid_object()
    {
        var dto = new ValidDto("Widget", 5);
        var result = ParameterValidator.ValidateObject(dto, "body");
        Assert.True(result.IsValid);
    }

    [Fact(Timeout = 5000)]
    public void Validate_should_fail_for_missing_required_field()
    {
        var dto = new ValidDto(null!, 5);
        var result = ParameterValidator.ValidateObject(dto, "body");
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Key == "Name");
    }

    [Fact(Timeout = 5000)]
    public void Validate_should_fail_for_range_violation()
    {
        var dto = new ValidDto("Widget", 200);
        var result = ParameterValidator.ValidateObject(dto, "body");
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Key == "Quantity");
    }

    [Fact(Timeout = 5000)]
    public void Validate_should_pass_for_object_without_annotations()
    {
        var dto = new PlainDto("anything");
        var result = ParameterValidator.ValidateObject(dto, "body");
        Assert.True(result.IsValid);
    }

    [Fact(Timeout = 5000)]
    public void Validate_should_collect_multiple_errors()
    {
        var dto = new ValidDto(null!, 200);
        var result = ParameterValidator.ValidateObject(dto, "body");
        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 2);
    }

    [Fact(Timeout = 5000)]
    public void HasValidationAttributes_should_return_true_for_annotated_type()
    {
        Assert.True(ParameterValidator.HasValidationAttributes(typeof(ValidDto)));
    }

    [Fact(Timeout = 5000)]
    public void HasValidationAttributes_should_return_false_for_plain_type()
    {
        Assert.False(ParameterValidator.HasValidationAttributes(typeof(PlainDto)));
    }

    public sealed record ValidDto(
        [property: Required] string Name,
        [property: Range(1, 100)] int Quantity);

    public sealed record PlainDto(string Name);
}
