using Clinic.Modules.Clinical.Contracts;
using Clinic.Modules.Clinical.Domain;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Clinical.Infrastructure;

internal static class ClinicalModel
{
    public const string Schema = "clinical";

    private static string In(IEnumerable<string> values) => string.Join(", ", values.Select(v => $"'{v}'"));

    public static void Configure(ModelBuilder model)
    {
        model.Entity<ProcedureType>(e =>
        {
            e.ToTable("procedure_types", Schema, t => t.HasCheckConstraint("ck_procedure_types_lab_model", $"lab_model IN ('{LabModels.General}', '{LabModels.Fixed}')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(60);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Category).HasMaxLength(60);
            e.Property(x => x.LabModel).HasMaxLength(10);
            e.Property(x => x.OptionDefinition).HasColumnType("jsonb");
            e.HasIndex(x => x.Code).IsUnique();
        });

        model.Entity<Visit>(e =>
        {
            e.ToTable("visits", Schema, t =>
            {
                t.HasCheckConstraint("ck_visits_status", $"status IN ({In(VisitStatuses.All)})");
                t.HasCheckConstraint("ck_visits_consultation_fee", "consultation_fee IS NULL OR consultation_fee >= 0");
                t.HasCheckConstraint("ck_visits_finalized", "(status = 'Finalized') = (finalized_at IS NOT NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.ClinicalNotes).HasMaxLength(8000);
            e.Property(x => x.ConsultationFee).HasPrecision(12, 2);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            e.HasAlternateKey(x => new { x.Id, x.PatientId }).HasName("ak_visits_id_patient_id");
            e.HasAlternateKey(x => new { x.Id, x.DoctorId }).HasName("ak_visits_id_doctor_id");
            e.HasAlternateKey(x => new { x.Id, x.PatientId, x.DoctorId }).HasName("ak_visits_id_patient_id_doctor_id");
            e.HasIndex(x => x.AppointmentId).IsUnique().HasFilter("appointment_id IS NOT NULL");
            e.HasIndex(x => new { x.PatientId, x.StartedAt }).IsDescending(false, true);
            e.HasIndex(x => new { x.DoctorId, x.StartedAt });
        });

        model.Entity<TreatmentCase>(e =>
        {
            e.ToTable("treatment_cases", Schema, t => t.HasCheckConstraint("ck_treatment_cases_status", $"status IN ({In(CaseStatuses.All)})"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Area).HasMaxLength(120);
            e.Property(x => x.Details).HasColumnType("jsonb");
            e.Property(x => x.LabModelSnapshot).HasMaxLength(10);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            e.HasAlternateKey(x => new { x.Id, x.PatientId }).HasName("ak_treatment_cases_id_patient_id");
            e.HasOne<ProcedureType>().WithMany().HasForeignKey(x => x.ProcedureTypeId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => new { x.StartedByDoctorId, x.Status });
        });

        model.Entity<TreatmentRecord>(e =>
        {
            e.ToTable("treatment_records", Schema);
            e.HasKey(x => x.Id);
            e.HasAlternateKey(x => new { x.Id, x.TreatmentCaseId, x.PatientId }).HasName("ak_treatment_records_id_case_patient");
            e.HasAlternateKey(x => new { x.Id, x.DoctorId }).HasName("ak_treatment_records_id_doctor_id");
            e.HasOne<Visit>().WithMany().HasForeignKey(x => new { x.VisitId, x.PatientId }).HasPrincipalKey(v => new { v.Id, v.PatientId })
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_treatment_records_visits_visit_patient");
            e.HasOne<Visit>().WithMany().HasForeignKey(x => new { x.VisitId, x.DoctorId }).HasPrincipalKey(v => new { v.Id, v.DoctorId })
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_treatment_records_visits_visit_doctor");
            e.HasOne<TreatmentCase>().WithMany().HasForeignKey(x => new { x.TreatmentCaseId, x.PatientId }).HasPrincipalKey(c => new { c.Id, c.PatientId })
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_treatment_records_cases_case_patient");
            e.HasIndex(x => new { x.VisitId, x.TreatmentCaseId }).IsUnique();
            e.HasIndex(x => x.TreatmentCaseId);
        });

        model.Entity<CaseWorkItem>(e =>
        {
            e.ToTable("case_work_items", Schema, t =>
            {
                t.HasCheckConstraint("ck_case_work_items_price", "entered_price > 0");
                t.HasCheckConstraint("ck_case_work_items_percentage", "doctor_percentage_snapshot BETWEEN 0 AND 100");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.EnteredPrice).HasPrecision(12, 2);
            e.Property(x => x.DoctorPercentageSnapshot).HasPrecision(5, 2);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            e.HasAlternateKey(x => new { x.Id, x.TreatmentCaseId, x.PerformingDoctorId }).HasName("ak_case_work_items_id_case_doctor");
            e.HasOne<TreatmentCase>().WithMany().HasForeignKey(x => new { x.TreatmentCaseId, x.PatientId }).HasPrincipalKey(c => new { c.Id, c.PatientId })
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_case_work_items_cases_case_patient");
            e.HasOne<TreatmentRecord>().WithMany().HasForeignKey(x => new { x.TreatmentRecordId, x.TreatmentCaseId, x.PatientId })
                .HasPrincipalKey(r => new { r.Id, r.TreatmentCaseId, r.PatientId })
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_case_work_items_records_record_case_patient");
            e.HasOne<TreatmentRecord>().WithMany().HasForeignKey(x => new { x.TreatmentRecordId, x.PerformingDoctorId })
                .HasPrincipalKey(r => new { r.Id, r.DoctorId })
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_case_work_items_records_record_doctor");
            e.HasIndex(x => x.TreatmentRecordId).IsUnique();
            e.HasIndex(x => x.TreatmentCaseId);
            e.HasIndex(x => new { x.PerformingDoctorId, x.CreatedAt });
        });

        model.Entity<TreatmentStep>(e =>
        {
            e.ToTable("treatment_steps", Schema, t =>
            {
                t.HasCheckConstraint("ck_treatment_steps_code", $"step_code IN ({In(StepCodes.All)})");
                t.HasCheckConstraint("ck_treatment_steps_status", $"status IN ('{StepStatuses.Pending}', '{StepStatuses.Completed}')");
                t.HasCheckConstraint("ck_treatment_steps_completion",
                    "(status = 'Completed') = (completed_at IS NOT NULL AND completed_in_visit_id IS NOT NULL AND completed_by_doctor_id IS NOT NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.StepCode).HasMaxLength(30);
            e.Property(x => x.Status).HasMaxLength(20);
            e.HasOne<TreatmentCase>().WithMany().HasForeignKey(x => x.TreatmentCaseId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Visit>().WithMany().HasForeignKey(x => x.CompletedInVisitId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.TreatmentCaseId, x.StepCode }).IsUnique();
        });

        model.Entity<RootCanalMeasurement>(e =>
        {
            e.ToTable("root_canal_measurements", Schema, t =>
            {
                t.HasCheckConstraint("ck_root_canal_measurements_canal", "canal_number BETWEEN 1 AND 5");
                t.HasCheckConstraint("ck_root_canal_measurements_length", "length_mm > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.LengthMm).HasPrecision(5, 2);
            e.HasOne<TreatmentCase>().WithMany().HasForeignKey(x => x.TreatmentCaseId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Visit>().WithMany().HasForeignKey(x => x.RecordedInVisitId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.TreatmentCaseId, x.CanalNumber, x.RecordedInVisitId }).IsUnique();
        });

        model.Entity<ImplantDetails>(e =>
        {
            e.ToTable("implant_details", Schema, t => t.HasCheckConstraint("ck_implant_details_healing", "healing_months BETWEEN 1 AND 6"));
            e.HasKey(x => x.TreatmentCaseId);
            e.HasOne<TreatmentCase>().WithOne().HasForeignKey<ImplantDetails>(x => x.TreatmentCaseId).OnDelete(DeleteBehavior.NoAction);
        });

        model.Entity<PricingDiscountEntry>(e =>
        {
            e.ToTable("pricing_discount_entries", Schema, t =>
            {
                t.HasCheckConstraint("ck_pricing_discount_entries_line",
                    "(pricing_line_kind = 'WorkItem' AND case_work_item_id IS NOT NULL AND consultation_visit_id IS NULL) OR " +
                    "(pricing_line_kind = 'Consultation' AND consultation_visit_id IS NOT NULL AND case_work_item_id IS NULL)");
                t.HasCheckConstraint("ck_pricing_discount_entries_amount", "amount >= 0");
                t.HasCheckConstraint("ck_pricing_discount_entries_seq", "entry_seq >= 1");
                t.HasCheckConstraint("ck_pricing_discount_entries_role", "set_by_role IN ('DOCTOR', 'SALES_RECEPTION', 'MANAGER')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.PricingLineKind).HasMaxLength(20);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.SetByRole).HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<CaseWorkItem>().WithMany().HasForeignKey(x => x.CaseWorkItemId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Visit>().WithMany().HasForeignKey(x => x.ConsultationVisitId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.CaseWorkItemId, x.EntrySeq }).IsUnique().HasFilter("case_work_item_id IS NOT NULL")
                .HasDatabaseName("ix_pricing_discount_entries_work_item_seq");
            e.HasIndex(x => new { x.ConsultationVisitId, x.EntrySeq }).IsUnique().HasFilter("consultation_visit_id IS NOT NULL")
                .HasDatabaseName("ix_pricing_discount_entries_consultation_seq");
        });

        model.Entity<VisitCorrection>(e =>
        {
            e.ToTable("visit_corrections", Schema, t =>
                t.HasCheckConstraint("ck_visit_corrections_record_type", $"record_type IN ({In(CorrectionRecordTypes.All)})"));
            e.HasKey(x => x.Id);
            e.Property(x => x.RecordType).HasMaxLength(30);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<Visit>().WithMany().HasForeignKey(x => new { x.VisitId, x.CorrectingDoctorId }).HasPrincipalKey(v => new { v.Id, v.DoctorId })
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_visit_corrections_visits_visit_doctor");
            e.HasIndex(x => new { x.VisitId, x.CorrectedAt });
        });
    }
}
