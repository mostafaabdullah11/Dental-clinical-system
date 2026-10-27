using Clinic.Modules.Crm.Contracts;
using Clinic.Modules.Crm.Domain;
using Clinic.Modules.Crm.Infrastructure;
using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Crm.Features;

// CRM is C-Contact data: Sales and Manager audiences only. No Doctor route, DTO or permission exists.

public sealed record SalesEmployeeRef(Guid Id, string Name);

[ContactData]
public sealed record LeadDto(
    Guid Id,
    string Name,
    string? Phone,
    string? Source,
    string? InterestedService,
    string? Notes,
    DateOnly? NextFollowUpDate,
    string Status,
    bool IsOpen,
    Guid? ConvertedPatientId,
    SalesEmployeeRef OriginalSalesEmployee,
    SalesEmployeeRef? ResponsibleSalesEmployee,
    DateTimeOffset CreatedAt,
    string Version);

[ContactData]
public sealed record ContactAttemptDto(Guid Id, DateTimeOffset AttemptedAt, Guid AttemptedByUserId, string Outcome, string? Notes);

[ContactData]
public sealed record FollowUpDto(
    Guid Id,
    string FollowUpType,
    PatientRef? Patient,
    Guid? LeadId,
    SalesEmployeeRef? AssignedSalesEmployee,
    DateOnly DueDate,
    string Status,
    bool IsOpen,
    string? Notes,
    string? Result,
    DateOnly? NextFollowUpDate,
    DateTimeOffset CreatedAt,
    string Version,
    IReadOnlyList<ContactAttemptDto>? Attempts,
    Guid? ChargeId = null,
    Guid? InstallmentId = null);

/// <summary>Lead input. SalesEmployeeId is accepted only from the Manager (Owner decision 2026-10-23); a Sales caller is the owner.</summary>
[ContactData]
public sealed record CreateLeadRequest(
    string Name, string? Phone, string? Source, string? InterestedService, string? Notes, DateOnly? NextFollowUpDate,
    string? Status, Guid? SalesEmployeeId);

/// <summary>Attribution, responsibility and conversion are not members: sending them is an unknown member (400).</summary>
[ContactData]
public sealed record UpdateLeadRequest(
    string Name, string? Phone, string? Source, string? InterestedService, string? Notes, DateOnly? NextFollowUpDate, string Status);

/// <summary>The patient created from the Lead. There is no Original Sales member: it is copied from the Lead.</summary>
[ContactData]
public sealed record ConvertLeadRequest(
    string FullName, DateOnly? DateOfBirth, int? AgeYearsRecorded, string? Phone, string? Whatsapp, string? Email,
    string? Address, string? CrmNotes);

public sealed record LeadConvertedDto(Guid LeadId, Guid PatientId, string PatientCode);

[ContactData]
public sealed record CreateFollowUpRequest(
    string FollowUpType, Guid? PatientId, Guid? LeadId, DateOnly? DueDate, string? Status, string? Notes, string? Result,
    DateOnly? NextFollowUpDate, Guid? SalesEmployeeId, Guid? ChargeId = null, Guid? InstallmentId = null);

[ContactData]
public sealed record UpdateFollowUpRequest(DateOnly? DueDate, string Status, string? Notes, string? Result, DateOnly? NextFollowUpDate);

[ContactData]
public sealed record AddContactAttemptRequest(string Outcome, string? Notes);

public sealed record AssignResponsibilityRequest(Guid? SalesEmployeeId);

internal static class CrmRules
{
    public static IRuleBuilderOptions<T, string?> LeadStatus<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(s => s is null || LeadStatuses.All.Contains(s)).WithErrorCode(FieldErrorCodes.Invalid);

    public static IRuleBuilderOptions<T, string?> FollowUpStatus<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(s => s is null || FollowUpStatuses.All.Contains(s)).WithErrorCode(FieldErrorCodes.Invalid);
}

internal sealed class CreateLeadRequestValidator : AbstractValidator<CreateLeadRequest>
{
    public CreateLeadRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone).MaximumLength(32);
        RuleFor(x => x.Source).MaximumLength(100);
        RuleFor(x => x.InterestedService).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Status).LeadStatus();
    }
}

internal sealed class UpdateLeadRequestValidator : AbstractValidator<UpdateLeadRequest>
{
    public UpdateLeadRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone).MaximumLength(32);
        RuleFor(x => x.Source).MaximumLength(100);
        RuleFor(x => x.InterestedService).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Status).NotEmpty().LeadStatus();
    }
}

internal sealed class ConvertLeadRequestValidator : AbstractValidator<ConvertLeadRequest>
{
    public ConvertLeadRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AgeYearsRecorded).InclusiveBetween(0, 120).When(x => x.AgeYearsRecorded is not null);
        RuleFor(x => x.DateOfBirth).NotNull().When(x => x.AgeYearsRecorded is null);
        RuleFor(x => x.Phone).MaximumLength(32);
        RuleFor(x => x.Whatsapp).MaximumLength(32);
        RuleFor(x => x.Email).MaximumLength(254);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.CrmNotes).MaximumLength(2000);
    }
}

internal sealed class CreateFollowUpRequestValidator : AbstractValidator<CreateFollowUpRequest>
{
    public CreateFollowUpRequestValidator()
    {
        RuleFor(x => x.FollowUpType).NotEmpty().Must(FollowUpTypes.All.Contains).WithErrorCode(FieldErrorCodes.Invalid);
        RuleFor(x => x.PatientId).NotNull().When(x => x.LeadId is null);
        RuleFor(x => x.PatientId).NotNull().When(x => x.ChargeId is not null || x.InstallmentId is not null);
        RuleFor(x => x.ChargeId).Null().When(x => x.LeadId is not null || x.InstallmentId is not null).WithErrorCode(FieldErrorCodes.Invalid);
        RuleFor(x => x.InstallmentId).Null().When(x => x.LeadId is not null).WithErrorCode(FieldErrorCodes.Invalid);
        RuleFor(x => x.DueDate).NotNull();
        RuleFor(x => x.Status).FollowUpStatus();
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Result).MaximumLength(2000);
    }
}

internal sealed class UpdateFollowUpRequestValidator : AbstractValidator<UpdateFollowUpRequest>
{
    public UpdateFollowUpRequestValidator()
    {
        RuleFor(x => x.DueDate).NotNull();
        RuleFor(x => x.Status).NotEmpty().FollowUpStatus();
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Result).MaximumLength(2000);
    }
}

internal sealed class AddContactAttemptRequestValidator : AbstractValidator<AddContactAttemptRequest>
{
    public AddContactAttemptRequestValidator()
    {
        RuleFor(x => x.Outcome).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

internal sealed class AssignResponsibilityRequestValidator : AbstractValidator<AssignResponsibilityRequest>
{
    public AssignResponsibilityRequestValidator() => RuleFor(x => x.SalesEmployeeId).NotNull();
}

/// <summary>Maps entities to audience DTOs with display names, without touching any clinical data.</summary>
internal sealed class CrmViews(DbContext db, IStaffDirectory staff, IPatientDirectory patients)
{
    public async Task<IReadOnlyList<LeadDto>> LeadsAsync(IReadOnlyList<Lead> leads, CancellationToken ct)
    {
        var open = await CrmPredicates.OpenLeadStatusesAsync(db, ct);
        var names = await staff.SalesEmployeeNamesAsync(
            leads.SelectMany(l => new[] { (Guid?)l.OriginalSalesEmployeeId, l.ResponsibleSalesEmployeeId }).OfType<Guid>(), ct);
        return leads.Select(l => new LeadDto(l.Id, l.Name, l.Phone, l.Source, l.InterestedService, l.Notes, l.NextFollowUpDate,
            l.Status, CrmPredicates.IsOpen(l, open), l.ConvertedPatientId,
            Ref(l.OriginalSalesEmployeeId, names)!, Ref(l.ResponsibleSalesEmployeeId, names), l.CreatedAt,
            ETags.Format(l.RowVersion))).ToList();
    }

    public async Task<LeadDto> LeadAsync(Lead lead, CancellationToken ct) => (await LeadsAsync([lead], ct))[0];

    public async Task<IReadOnlyList<FollowUpDto>> FollowUpsAsync(IReadOnlyList<FollowUp> items, bool withAttempts, CancellationToken ct)
    {
        var names = await staff.SalesEmployeeNamesAsync(items.Select(f => f.AssignedSalesEmployeeId).OfType<Guid>(), ct);
        var patientRefs = await patients.FindManyAsync(items.Select(f => f.PatientId).OfType<Guid>(), ct);
        var ids = items.Select(f => f.Id).ToList();
        var attempts = withAttempts
            ? (await db.Set<FollowUpContactAttempt>().AsNoTracking().Where(a => ids.Contains(a.FollowUpId))
                    .OrderBy(a => a.AttemptedAt).ThenBy(a => a.Id).ToListAsync(ct))
                .GroupBy(a => a.FollowUpId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<ContactAttemptDto>)g
                    .Select(a => new ContactAttemptDto(a.Id, a.AttemptedAt, a.AttemptedByUserId, a.Outcome, a.Notes)).ToList())
            : null;

        return items.Select(f => new FollowUpDto(f.Id, f.FollowUpType,
            f.PatientId is { } p ? patientRefs.GetValueOrDefault(p) : null, f.LeadId, Ref(f.AssignedSalesEmployeeId, names),
            f.DueDate, f.Status, FollowUpStatuses.IsOpen(f.Status), f.Notes, f.Result, f.NextFollowUpDate, f.CreatedAt,
            ETags.Format(f.RowVersion), attempts is null ? null : attempts.GetValueOrDefault(f.Id) ?? [], f.ChargeId, f.InstallmentId)).ToList();
    }

    private static SalesEmployeeRef? Ref(Guid? id, IReadOnlyDictionary<Guid, string> names) =>
        id is { } value ? new SalesEmployeeRef(value, names.GetValueOrDefault(value) ?? string.Empty) : null;
}
