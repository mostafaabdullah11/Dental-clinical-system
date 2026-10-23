using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;

namespace Clinic.Modules.Patients.Domain.Contact;

/// <summary>
/// patients.patient_contacts (M, restricted A). C-Contact data. Doctor-facing code never touches this table
/// (D1, D2). patient_source and original_sales_employee_id are frozen after creation (no UPDATE grant).
/// </summary>
[ContactData]
[RestrictedAudit]
internal sealed class PatientContact : IAuditable, IRowVersioned
{
    public Guid PatientId { get; init; }
    public string? Phone { get; set; }
    public string? Whatsapp { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? CrmNotes { get; set; }
    public required string PatientSource { get; init; }
    public Guid? OriginalSalesEmployeeId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public uint RowVersion { get; private set; }
}
