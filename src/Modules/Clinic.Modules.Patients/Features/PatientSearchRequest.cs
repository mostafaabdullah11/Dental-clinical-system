using FluentValidation;

namespace Clinic.Modules.Patients.Features;

/// <summary>
/// Patient search is always a POST body, never a query string (FR-PAT-004, SECURITY_DESIGN.md §11).
/// Page size is capped at 50 for patient search (API A5).
/// </summary>
public sealed record PatientSearchRequest(string Query, int? Page, int? PageSize)
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 50;

    public int PageOrDefault => Page ?? 1;
    public int PageSizeOrDefault => PageSize ?? DefaultPageSize;
}

internal sealed class PatientSearchRequestValidator : AbstractValidator<PatientSearchRequest>
{
    public PatientSearchRequestValidator()
    {
        RuleFor(x => x.Query).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).When(x => x.Page is not null);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PatientSearchRequest.MaxPageSize).When(x => x.PageSize is not null);
    }
}
