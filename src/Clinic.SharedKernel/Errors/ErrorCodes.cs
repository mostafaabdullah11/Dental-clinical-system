namespace Clinic.SharedKernel.Errors;

/// <summary>
/// Technical (non-business) error codes used by the RFC 7807 contract (API A6).
/// Business error codes are added only when the FRS defines them; none of these is a business rule.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string MalformedRequest = "malformed_request";
    public const string Unauthenticated = "unauthenticated";
    public const string InvalidCredentials = "auth.invalid_credentials";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string MethodNotAllowed = "method_not_allowed";
    public const string Conflict = "conflict";
    public const string RuleViolation = "rule_violation";
    public const string PreconditionRequired = "concurrency.precondition_required";

    /// <summary>Defined in API A6.</summary>
    public const string ConcurrencyStale = "concurrency.stale";

    public const string CsrfInvalid = "csrf_invalid";
    public const string RateLimited = "rate_limited";
    public const string InternalError = "internal_error";
}

/// <summary>Field-level validation codes (never the submitted value).</summary>
public static class FieldErrorCodes
{
    public const string Required = "required";
    public const string TooShort = "too_short";
    public const string TooLong = "too_long";
    public const string OutOfRange = "out_of_range";
    public const string Invalid = "invalid";
    public const string Duplicate = "duplicate";
    public const string NotAllowed = "not_allowed";
    public const string PasswordDenied = "password_denied";
    public const string RoleMismatch = "role_mismatch";
    public const string EditableVisitRequired = "editable_visit_required";
}
