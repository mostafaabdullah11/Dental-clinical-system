using Clinic.Modules.Prescriptions.Domain;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Prescriptions.Infrastructure;

internal static class PrescriptionsModel
{
    public const string Schema = "prescriptions";

    public static void Configure(ModelBuilder model)
    {
        model.Entity<Prescription>(e =>
        {
            e.ToTable("prescriptions", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.GeneralNotes).HasMaxLength(2000);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            e.HasIndex(x => new { x.PatientId, x.CreatedAt }).IsDescending(false, true);
            e.HasIndex(x => x.VisitId);
        });

        model.Entity<PrescriptionItem>(e =>
        {
            e.ToTable("prescription_items", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.MedicationName).HasMaxLength(200);
            e.Property(x => x.Dosage).HasMaxLength(200);
            e.Property(x => x.Frequency).HasMaxLength(200);
            e.Property(x => x.Duration).HasMaxLength(200);
            e.Property(x => x.Instructions).HasMaxLength(1000);
            e.HasOne<Prescription>().WithMany().HasForeignKey(x => x.PrescriptionId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.PrescriptionId);
        });
    }
}
