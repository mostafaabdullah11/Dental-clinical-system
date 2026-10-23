namespace Clinic.SharedKernel.Errors;

/// <summary>
/// A refused request. Translated to RFC 7807 problem+json by the API host.
/// Field errors carry only field names and codes, never submitted values.
/// </summary>
public class AppException : Exception
{
    public AppException(int status, string code, IReadOnlyDictionary<string, string[]>? fieldErrors = null)
        : base(code)
    {
        Status = status;
        Code = code;
        FieldErrors = fieldErrors;
    }

    public int Status { get; }
    public string Code { get; }
    public IReadOnlyDictionary<string, string[]>? FieldErrors { get; }

    public static AppException Validation(string field, string fieldCode) =>
        new(400, ErrorCodes.ValidationFailed, new Dictionary<string, string[]> { [field] = [fieldCode] });

    public static AppException NotFound() => new(404, ErrorCodes.NotFound);

    public static AppException Conflict(string field, string fieldCode) =>
        new(409, ErrorCodes.Conflict, new Dictionary<string, string[]> { [field] = [fieldCode] });

    public static AppException Rule(string field, string fieldCode) =>
        new(422, ErrorCodes.RuleViolation, new Dictionary<string, string[]> { [field] = [fieldCode] });

    public static AppException PreconditionRequired() => new(428, ErrorCodes.PreconditionRequired);

    public static AppException Stale() => new(412, ErrorCodes.ConcurrencyStale);
}
