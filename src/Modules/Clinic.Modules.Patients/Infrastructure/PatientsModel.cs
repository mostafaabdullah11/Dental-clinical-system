using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Patients.Domain;
using Clinic.Modules.Patients.Domain.Contact;
using Clinic.Modules.Patients.Domain.MedicalHistory;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Patients.Infrastructure;

internal static class PatientsModel
{
    public const string Schema = "patients";
    public const string PatientCodeSequence = "patient_code_seq";

    public static void Configure(ModelBuilder model)
    {
        model.HasSequence<long>(PatientCodeSequence, Schema);

        model.Entity<Patient>(e =>
        {
            e.ToTable("patients", Schema, t =>
            {
                t.HasCheckConstraint("ck_patients_age_pair", "(age_years_recorded IS NULL) = (age_recorded_on IS NULL)");
                t.HasCheckConstraint("ck_patients_age_range", "age_years_recorded IS NULL OR (age_years_recorded >= 0 AND age_years_recorded <= 120)");
                t.HasCheckConstraint("ck_patients_dob_or_age", "date_of_birth IS NOT NULL OR age_years_recorded IS NOT NULL");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.PatientCode).HasMaxLength(20)
                .HasDefaultValueSql($"'P-' || lpad(nextval('{Schema}.{PatientCodeSequence}')::text, 6, '0')")
                .ValueGeneratedOnAdd();
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.ChiefComplaint).HasMaxLength(2000);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            e.HasIndex(x => x.PatientCode).IsUnique();
        });

        model.Entity<PatientContact>(e =>
        {
            e.ToTable("patient_contacts", Schema, t => t.HasCheckConstraint("ck_patient_contacts_source",
                $"(patient_source = '{PatientSources.WalkIn}' AND original_sales_employee_id IS NULL) OR " +
                $"(patient_source = '{PatientSources.Lead}' AND original_sales_employee_id IS NOT NULL)"));
            e.HasKey(x => x.PatientId);
            e.Property(x => x.Phone).HasMaxLength(32);
            e.Property(x => x.Whatsapp).HasMaxLength(32);
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.CrmNotes).HasMaxLength(2000);
            e.Property(x => x.PatientSource).HasMaxLength(10);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            e.HasOne<Patient>().WithOne().HasForeignKey<PatientContact>(x => x.PatientId).OnDelete(DeleteBehavior.NoAction);
        });

        model.Entity<PatientMedicalHistory>(e =>
        {
            e.ToTable("patient_medical_histories", Schema, t => t.HasCheckConstraint(
                "ck_patient_medical_histories_last_dental_visit", "last_dental_visit IS NULL OR char_length(last_dental_visit) <= 200"));
            e.HasKey(x => x.PatientId);
            e.Property(x => x.LastDentalVisit).HasMaxLength(200);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            e.HasOne<Patient>().WithOne().HasForeignKey<PatientMedicalHistory>(x => x.PatientId).OnDelete(DeleteBehavior.NoAction);
        });

        model.Entity<PatientDoctorAssignment>(e =>
        {
            e.ToTable("patient_doctor_assignments", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasIndex(x => x.PatientId).IsUnique().HasFilter("ended_at IS NULL").HasDatabaseName("ux_patient_doctor_assignments_current");
            e.HasIndex(x => new { x.PatientId, x.AssignedAt }).IsDescending(false, true);
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
