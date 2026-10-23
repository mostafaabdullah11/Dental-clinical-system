using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Patients.Contracts.Contact;
using Clinic.Modules.Patients.Infrastructure;
using Clinic.Modules.Patients.Infrastructure.Clinical;
using Clinic.Modules.Patients.Infrastructure.Contact;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using FluentValidation;

namespace Clinic.Modules.Patients.Features.Shared;

// Sales / Manager audiences only (C-Contact allowed, C-Clinical forbidden for Sales). Never used by Doctor features.

public sealed record SalesEmployeeRefDto(Guid Id, string Name);

[ContactData]
public sealed record PatientContactListItem(Guid Id, string PatientCode, string FullName, AgeDisplay AgeDisplay, string? Phone);

/// <summary>
/// Walk-In registration (FR-PAT-001): name, DOB or recorded age, contact fields. There is no Original Sales
/// Employee member and no medical field: such members are unknown and rejected (400).
/// </summary>
[ContactData]
public sealed record RegisterPatientRequest(
    string FullName,
    DateOnly? DateOfBirth,
    int? AgeYearsRecorded,
    string? Phone,
    string? Whatsapp,
    string? Email,
    string? Address,
    string? CrmNotes);

internal static class ContactRules
{
    public static void Apply<T>(AbstractValidator<T> v, Func<T, string?> phone, Func<T, string?> whatsapp, Func<T, string?> email,
        Func<T, string?> address, Func<T, string?> crmNotes)
    {
        v.RuleFor(x => phone(x)).MaximumLength(32).OverridePropertyName("phone");
        v.RuleFor(x => whatsapp(x)).MaximumLength(32).OverridePropertyName("whatsapp");
        v.RuleFor(x => email(x)).MaximumLength(254).OverridePropertyName("email");
        v.RuleFor(x => address(x)).MaximumLength(500).OverridePropertyName("address");
        v.RuleFor(x => crmNotes(x)).MaximumLength(2000).OverridePropertyName("crmNotes");
    }
}

internal sealed class RegisterPatientRequestValidator : AbstractValidator<RegisterPatientRequest>
{
    public RegisterPatientRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AgeYearsRecorded).InclusiveBetween(PatientIdentityRules.MinAge, PatientIdentityRules.MaxAge)
            .When(x => x.AgeYearsRecorded is not null);
        RuleFor(x => x.DateOfBirth).NotNull().When(x => x.AgeYearsRecorded is null);
        ContactRules.Apply(this, x => x.Phone, x => x.Whatsapp, x => x.Email, x => x.Address, x => x.CrmNotes);
    }
}

/// <summary>Everything the Sales and Manager patient views share; built only from the two readers.</summary>
internal sealed record PatientContactView(
    ClinicalPatientRecord Clinical,
    ContactRecord Contact,
    AgeDisplay AgeDisplay,
    SalesEmployeeRefDto? OriginalSalesEmployee,
    CurrentDoctorDto? CurrentDoctor,
    decimal? PatientCreditBalance);

internal sealed class PatientContactViews(
    IPatientClinicalReader clinical,
    IPatientContactReader contacts,
    PatientPresentation presentation,
    IStaffDirectory staff,
    IPatientCreditBalanceReader credit)
{
    public async Task<PatientContactView> GetAsync(Guid patientId, CancellationToken ct)
    {
        var patient = await clinical.FindAsync(patientId, ct) ?? throw AppException.NotFound();
        var contact = await contacts.FindAsync(patientId, ct) ?? throw AppException.NotFound();
        var today = await presentation.TodayAsync(ct);
        var doctors = await presentation.DoctorsAsync([patient.CurrentDoctorId], ct);

        // The Original Sales Employee is shown only for Lead-sourced patients (FR-PAT-006).
        SalesEmployeeRefDto? original = null;
        if (contact is { PatientSource: PatientSources.Lead, OriginalSalesEmployeeId: { } salesId })
        {
            var names = await staff.SalesEmployeeNamesAsync([salesId], ct);
            original = new SalesEmployeeRefDto(salesId, names.GetValueOrDefault(salesId) ?? string.Empty);
        }

        return new PatientContactView(patient, contact,
            PatientPresentation.Age(patient.DateOfBirth, patient.AgeYearsRecorded, patient.AgeRecordedOn, today),
            original,
            patient.CurrentDoctorId is { } d ? doctors.GetValueOrDefault(d) : null,
            await credit.GetCreditBalanceAsync(patientId, ct));
    }

    public async Task<IReadOnlyList<PatientContactListItem>> ListAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var today = await presentation.TodayAsync(ct);
        var contactRows = await contacts.FindManyAsync(ids, ct);
        var items = new List<PatientContactListItem>(ids.Count);
        foreach (var id in ids)
        {
            var p = await clinical.FindAsync(id, ct);
            if (p is null)
            {
                continue;
            }

            items.Add(new PatientContactListItem(p.Id, p.PatientCode, p.FullName,
                PatientPresentation.Age(p.DateOfBirth, p.AgeYearsRecorded, p.AgeRecordedOn, today),
                contactRows.GetValueOrDefault(id)?.Phone));
        }

        return items;
    }

    public static PatientContactInput ContactInput(RegisterPatientRequest r) => new(r.Phone, r.Whatsapp, r.Email, r.Address, r.CrmNotes);

    public static string Version(uint rowVersion) => ETags.Format(rowVersion);
}
