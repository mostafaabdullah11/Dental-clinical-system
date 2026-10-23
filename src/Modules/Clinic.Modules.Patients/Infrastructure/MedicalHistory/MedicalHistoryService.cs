using Clinic.Modules.Patients.Domain;
using Clinic.Modules.Patients.Domain.MedicalHistory;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Patients.Infrastructure.MedicalHistory;

/// <summary>The structured answers exactly as confirmed (FR-MED-001); no other field exists.</summary>
[MedicalData]
internal sealed record MedicalHistoryAnswers(
    bool? HasAllergy,
    bool? HasHypertension,
    bool? HasViralHepatitis,
    bool? HasDiabetes,
    bool? HasKidneyProblems,
    bool? HasBleedingOrClottingProblem,
    bool? HasHeartProblems,
    bool? HasRheumaticDisease,
    bool? IsSmoker,
    bool? HasPreviousSurgicalOperations,
    bool? IsTakingMedications,
    string? LastDentalVisit,
    bool? IsPregnant,
    bool? IsBreastfeeding);

internal sealed record StoredMedicalHistory(MedicalHistoryAnswers Answers, DateTimeOffset UpdatedAt, uint RowVersion);

/// <summary>
/// Intake create / replace (FR-MED-002) and read (FR-MED-003). Audit rows carry actor, time, patient id and the
/// changed field NAMES only — never a value (FR-MED-006). The intake is independent of any appointment state.
/// </summary>
internal sealed class MedicalHistoryService(DbContext db, IAuditWriter audit, IClock clock)
{
    public const int LastDentalVisitMaxLength = 200;

    /// <summary>API field names, in the confirmed order.</summary>
    public static readonly IReadOnlyList<(string Name, Func<MedicalHistoryAnswers, object?> Get)> Fields =
    [
        ("hasAllergy", a => a.HasAllergy),
        ("hasHypertension", a => a.HasHypertension),
        ("hasViralHepatitis", a => a.HasViralHepatitis),
        ("hasDiabetes", a => a.HasDiabetes),
        ("hasKidneyProblems", a => a.HasKidneyProblems),
        ("hasBleedingOrClottingProblem", a => a.HasBleedingOrClottingProblem),
        ("hasHeartProblems", a => a.HasHeartProblems),
        ("hasRheumaticDisease", a => a.HasRheumaticDisease),
        ("isSmoker", a => a.IsSmoker),
        ("hasPreviousSurgicalOperations", a => a.HasPreviousSurgicalOperations),
        ("isTakingMedications", a => a.IsTakingMedications),
        ("lastDentalVisit", a => a.LastDentalVisit),
        ("isPregnant", a => a.IsPregnant),
        ("isBreastfeeding", a => a.IsBreastfeeding),
    ];

    public async Task<StoredMedicalHistory?> FindAsync(Guid patientId, CancellationToken ct)
    {
        var row = await db.Set<PatientMedicalHistory>().AsNoTracking().SingleOrDefaultAsync(m => m.PatientId == patientId, ct);
        return row is null ? null : new StoredMedicalHistory(ToAnswers(row), row.UpdatedAt, row.RowVersion);
    }

    /// <summary>First PUT creates (no If-Match); later PUTs replace the answers and require a current If-Match.</summary>
    public async Task<StoredMedicalHistory> PutAsync(Guid patientId, MedicalHistoryAnswers answers, HttpRequest request, CancellationToken ct)
    {
        if (!await db.Set<Patient>().AnyAsync(p => p.Id == patientId, ct))
        {
            throw AppException.NotFound();
        }

        if (answers.LastDentalVisit is { Length: > LastDentalVisitMaxLength })
        {
            throw AppException.Validation("lastDentalVisit", FieldErrorCodes.TooLong);
        }

        var now = clock.UtcNow;
        var row = await db.Set<PatientMedicalHistory>().SingleOrDefaultAsync(m => m.PatientId == patientId, ct);
        string action;
        List<string> changed;
        if (row is null)
        {
            row = new PatientMedicalHistory { PatientId = patientId, CreatedAt = now };
            Apply(row, answers);
            row.UpdatedAt = now;
            db.Set<PatientMedicalHistory>().Add(row);
            action = "patients.medical_history.created";
            changed = Fields.Where(f => f.Get(answers) is not null).Select(f => f.Name).ToList();
        }
        else
        {
            ETags.Require(request, row.RowVersion);
            var before = ToAnswers(row);
            changed = Fields.Where(f => !Equals(f.Get(before), f.Get(answers))).Select(f => f.Name).ToList();
            Apply(row, answers);
            row.UpdatedAt = now;
            action = "patients.medical_history.updated";
        }

        audit.Add(new AuditEvent(action, "patients.patient_medical_histories", patientId.ToString(),
            NewValue: new { changedFields = changed }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw AppException.Stale();
        }

        return new StoredMedicalHistory(ToAnswers(row), row.UpdatedAt, row.RowVersion);
    }

    private static void Apply(PatientMedicalHistory row, MedicalHistoryAnswers a)
    {
        row.HasAllergy = a.HasAllergy;
        row.HasHypertension = a.HasHypertension;
        row.HasViralHepatitis = a.HasViralHepatitis;
        row.HasDiabetes = a.HasDiabetes;
        row.HasKidneyProblems = a.HasKidneyProblems;
        row.HasBleedingOrClottingProblem = a.HasBleedingOrClottingProblem;
        row.HasHeartProblems = a.HasHeartProblems;
        row.HasRheumaticDisease = a.HasRheumaticDisease;
        row.IsSmoker = a.IsSmoker;
        row.HasPreviousSurgicalOperations = a.HasPreviousSurgicalOperations;
        row.IsTakingMedications = a.IsTakingMedications;
        row.LastDentalVisit = a.LastDentalVisit;
        row.IsPregnant = a.IsPregnant;
        row.IsBreastfeeding = a.IsBreastfeeding;
    }

    private static MedicalHistoryAnswers ToAnswers(PatientMedicalHistory m) => new(
        m.HasAllergy, m.HasHypertension, m.HasViralHepatitis, m.HasDiabetes, m.HasKidneyProblems,
        m.HasBleedingOrClottingProblem, m.HasHeartProblems, m.HasRheumaticDisease, m.IsSmoker,
        m.HasPreviousSurgicalOperations, m.IsTakingMedications, m.LastDentalVisit, m.IsPregnant, m.IsBreastfeeding);
}
