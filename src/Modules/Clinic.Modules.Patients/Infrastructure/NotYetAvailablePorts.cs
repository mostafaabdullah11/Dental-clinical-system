using Clinic.Modules.Patients.Contracts;

namespace Clinic.Modules.Patients.Infrastructure;

/// <summary>
/// TECHNICAL placeholder until Finance (Task 4) registers the real reader: returns null = "not available".
/// It never reports a balance, so no Patient Credit figure is fabricated.
/// </summary>
internal sealed class FinanceNotAvailableCreditReader : IPatientCreditBalanceReader
{
    public Task<decimal?> GetCreditBalanceAsync(Guid patientId, CancellationToken cancellationToken) =>
        Task.FromResult<decimal?>(null);
}

/// <summary>
/// TECHNICAL placeholder until Clinical (Task 3) registers the real port. No Visit can exist yet, so a Doctor
/// never has an editable Visit and the FR-PAT-008 precondition refuses every Doctor write.
/// </summary>
internal sealed class ClinicalNotAvailableVisitPort : IEditableVisitPort
{
    public Task<bool> DoctorHasEditableVisitAsync(Guid doctorUserId, Guid patientId, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
