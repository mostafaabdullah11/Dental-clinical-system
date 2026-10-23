using Clinic.SharedKernel.Errors;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.SharedKernel.Validation;

/// <summary>
/// Runs the FluentValidation validator for <typeparamref name="T"/> after authorization (API A4) and
/// returns field names with codes only, never the submitted values.
/// </summary>
public sealed class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var validator = context.HttpContext.RequestServices.GetService<IValidator<T>>();
        var argument = context.Arguments.OfType<T>().FirstOrDefault();
        if (validator is not null)
        {
            if (argument is null)
            {
                throw new AppException(400, ErrorCodes.MalformedRequest);
            }

            var result = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);
            if (!result.IsValid)
            {
                throw new AppException(400, ErrorCodes.ValidationFailed, ToFieldErrors(result));
            }
        }

        return await next(context);
    }

    private static Dictionary<string, string[]> ToFieldErrors(ValidationResult result) =>
        result.Errors
            .GroupBy(e => ToCamelCase(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => MapCode(e.ErrorCode)).Distinct().ToArray());

    private static string MapCode(string errorCode) => errorCode switch
    {
        "NotEmptyValidator" or "NotNullValidator" => FieldErrorCodes.Required,
        "MinimumLengthValidator" => FieldErrorCodes.TooShort,
        "MaximumLengthValidator" => FieldErrorCodes.TooLong,
        "InclusiveBetweenValidator" or "ExclusiveBetweenValidator" or "GreaterThanValidator"
            or "GreaterThanOrEqualValidator" or "LessThanValidator" or "LessThanOrEqualValidator"
            or "PrecisionScaleValidator" => FieldErrorCodes.OutOfRange,
        "NullValidator" or "EmptyValidator" => FieldErrorCodes.NotAllowed,
        _ when errorCode.Contains('_', StringComparison.Ordinal) || errorCode.All(char.IsLower) => errorCode,
        _ => FieldErrorCodes.Invalid,
    };

    private static string ToCamelCase(string name) =>
        string.Join('.', name.Split('.').Select(p => p.Length == 0 ? p : char.ToLowerInvariant(p[0]) + p[1..]));
}

public static class ValidationExtensions
{
    public static RouteHandlerBuilder Validate<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter<ValidationFilter<T>>();

    /// <summary>Money / percentage scale rule: at most two decimals.</summary>
    public static IRuleBuilderOptions<TModel, decimal> TwoDecimals<TModel>(this IRuleBuilder<TModel, decimal> rule) =>
        rule.PrecisionScale(12, 2, ignoreTrailingZeros: true);
}
