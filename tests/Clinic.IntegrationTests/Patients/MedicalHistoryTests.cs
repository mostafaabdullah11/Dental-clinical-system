using System.Net;
using System.Text.Json;
using Clinic.IntegrationTests.Infrastructure;
using Xunit;

namespace Clinic.IntegrationTests.Patients;

/// <summary>AC-MED-001 … AC-MED-008 (AC-MED-007 partially: booking / arrival / Visit start arrive in Task 3).</summary>
public sealed class MedicalHistoryTests(ApiFixture fixture)
{
    private static readonly string[] Fields =
    [
        "hasAllergy", "hasHypertension", "hasViralHepatitis", "hasDiabetes", "hasKidneyProblems", "hasBleedingOrClottingProblem",
        "hasHeartProblems", "hasRheumaticDisease", "isSmoker", "hasPreviousSurgicalOperations", "isTakingMedications",
        "lastDentalVisit", "isPregnant", "isBreastfeeding",
    ];

    [Fact] // AC-MED-001 (a) / AC-MED-002 (a)(b): 404 before; first PUT creates without If-Match; exactly the fields
    public async Task First_put_creates_the_intake_with_exactly_the_confirmed_fields()
    {
        var (sales, patient) = await SalesWithPatientAsync();

        await Http.ProblemAsync(await sales.GetAsync($"/api/sales/patients/{patient.Id}/medical-history"), HttpStatusCode.NotFound);

        var created = await Http.JsonAsync(await sales.PutAsync($"/api/sales/patients/{patient.Id}/medical-history", new
        {
            hasAllergy = true, hasHypertension = false, hasViralHepatitis = (bool?)null, hasDiabetes = true, hasKidneyProblems = false,
            hasBleedingOrClottingProblem = false, hasHeartProblems = false, hasRheumaticDisease = false, isSmoker = true,
            hasPreviousSurgicalOperations = false, isTakingMedications = true, lastDentalVisit = "منذ ستة أشهر",
            isPregnant = (bool?)null, isBreastfeeding = (bool?)null,
        }));

        Assert.Equal(Fields.Concat(["etag", "patientId", "updatedAt"]).Order(), created.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("منذ ستة أشهر", created.GetProperty("lastDentalVisit").GetString());

        var read = await sales.GetAsync($"/api/sales/patients/{patient.Id}/medical-history");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.NotNull(read.Headers.ETag);
    }

    [Fact] // AC-MED-004: unanswered stays null, never false (API and DB)
    public async Task Unanswered_flags_round_trip_as_null_never_false()
    {
        var (sales, patient) = await SalesWithPatientAsync();

        var created = await Http.JsonAsync(await sales.PutAsync($"/api/sales/patients/{patient.Id}/medical-history",
            new { hasDiabetes = true, isSmoker = false }));

        Assert.True(created.GetProperty("hasDiabetes").GetBoolean());
        Assert.False(created.GetProperty("isSmoker").GetBoolean());
        foreach (var field in Fields.Except(["hasDiabetes", "isSmoker"]))
        {
            Assert.Equal(JsonValueKind.Null, created.GetProperty(field).ValueKind);
        }

        Assert.Equal(12, await fixture.Database.ScalarAsync<long>($"""
            SELECT (has_allergy IS NULL)::int + (has_hypertension IS NULL)::int + (has_viral_hepatitis IS NULL)::int
                 + (has_kidney_problems IS NULL)::int + (has_bleeding_or_clotting_problem IS NULL)::int + (has_heart_problems IS NULL)::int
                 + (has_rheumatic_disease IS NULL)::int + (has_previous_surgical_operations IS NULL)::int
                 + (is_taking_medications IS NULL)::int + (last_dental_visit IS NULL)::int + (is_pregnant IS NULL)::int
                 + (is_breastfeeding IS NULL)::int
            FROM patients.patient_medical_histories WHERE patient_id = '{patient.Id}'
            """));
    }

    [Fact] // AC-MED-004: flag columns have no DEFAULT and no NOT NULL
    public async Task Flag_columns_are_nullable_without_defaults()
    {
        var rows = await fixture.Database.ColumnAsync("""
            SELECT column_name || '|' || is_nullable || '|' || coalesce(column_default, '')
            FROM information_schema.columns
            WHERE table_schema = 'patients' AND table_name = 'patient_medical_histories'
              AND (data_type = 'boolean' OR column_name = 'last_dental_visit')
            """);
        Assert.Equal(14, rows.Count);
        Assert.All(rows, r => Assert.EndsWith("|YES|", r));
    }

    [Fact] // AC-MED-002 (c)(d): replace needs If-Match; stale → 412; flags accept only true/false/null
    public async Task Later_puts_replace_answers_with_if_match()
    {
        var (sales, patient) = await SalesWithPatientAsync();
        var created = await sales.PutAsync($"/api/sales/patients/{patient.Id}/medical-history", new { hasAllergy = true });
        var etag = created.Headers.ETag!.Tag;

        await Http.ProblemAsync(await sales.PutAsync($"/api/sales/patients/{patient.Id}/medical-history", new { hasAllergy = false }), (HttpStatusCode)428);

        var replaced = await sales.PutAsync($"/api/sales/patients/{patient.Id}/medical-history", new { hasAllergy = false }, etag);
        Assert.False((await Http.JsonAsync(replaced)).GetProperty("hasAllergy").GetBoolean());

        await Http.ProblemAsync(await sales.PutAsync($"/api/sales/patients/{patient.Id}/medical-history", new { hasAllergy = true }, etag), HttpStatusCode.PreconditionFailed);

        foreach (var bad in new[] { "\"yes\"", "1", "\"true\"" })
        {
            await Http.ProblemAsync(await sales.PutAsync($"/api/sales/patients/{patient.Id}/medical-history",
                $"{{\"hasAllergy\":{bad}}}", replaced.Headers.ETag!.Tag), HttpStatusCode.BadRequest);
        }

        // Setting a flag back to null is accepted and reads back as null.
        var cleared = await Http.JsonAsync(await sales.PutAsync($"/api/sales/patients/{patient.Id}/medical-history",
            new { hasAllergy = (bool?)null }, replaced.Headers.ETag!.Tag));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("hasAllergy").ValueKind);
    }

    [Fact] // AC-MED-001 (b)(c) / AC-NEG-032: 200-char limit; no detail / generic / sex members
    public async Task Last_dental_visit_limit_and_unknown_members()
    {
        var (sales, patient) = await SalesWithPatientAsync();

        var tooLong = await sales.PutAsync($"/api/sales/patients/{patient.Id}/medical-history", new { lastDentalVisit = new string('x', 201) });
        Assert.Contains("too_long", (await Http.ProblemAsync(tooLong, HttpStatusCode.BadRequest)).FieldCodes("lastDentalVisit"));

        foreach (var member in new[] { "allergyType", "medicationNames", "medicationDoses", "surgeryDetails", "otherCondition", "medicalHistory", "chronicDiseases", "previousSurgeries", "sex", "gender", "hepatitisDetails" })
        {
            await Http.ProblemAsync(await sales.PutAsync($"/api/sales/patients/{patient.Id}/medical-history", $"{{\"{member}\":\"x\"}}"), HttpStatusCode.BadRequest);
        }

        // Free text is never coerced to a date.
        foreach (var answer in new[] { "6 months ago", "last year", "I don't remember", new string('ع', 200) })
        {
            var fresh = await SalesWithPatientAsync();
            var ok = await Http.JsonAsync(await fresh.Client.PutAsync($"/api/sales/patients/{fresh.Patient.Id}/medical-history", new { lastDentalVisit = answer }));
            Assert.Equal(answer, ok.GetProperty("lastDentalVisit").GetString());
        }
    }

    [Fact] // AC-MED-005: pregnancy / breastfeeding stored as given; not applicable stays null; no sex field anywhere
    public async Task Pregnancy_and_breastfeeding_are_optional_tri_state()
    {
        var (sales, patient) = await SalesWithPatientAsync();
        var answered = await Http.JsonAsync(await sales.PutAsync($"/api/sales/patients/{patient.Id}/medical-history", new { isPregnant = false, isBreastfeeding = true }));
        Assert.False(answered.GetProperty("isPregnant").GetBoolean());
        Assert.True(answered.GetProperty("isBreastfeeding").GetBoolean());

        var other = await SalesWithPatientAsync();
        var notApplicable = await Http.JsonAsync(await other.Client.PutAsync($"/api/sales/patients/{other.Patient.Id}/medical-history", new { hasAllergy = false }));
        Assert.Equal(JsonValueKind.Null, notApplicable.GetProperty("isPregnant").ValueKind);
        Assert.Equal(JsonValueKind.Null, notApplicable.GetProperty("isBreastfeeding").ValueKind);

        Assert.Equal(0, await fixture.Database.ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'patients' AND (column_name ILIKE '%sex%' OR column_name ILIKE '%gender%')"));
    }

    [Fact] // AC-MED-003: Doctor reads read-only (no contact, no ETag); every write attempt → 403; read audited
    public async Task Doctor_reads_the_intake_read_only()
    {
        var (sales, patient) = await SalesWithPatientAsync();
        await sales.PutAsync($"/api/sales/patients/{patient.Id}/medical-history", new { hasDiabetes = true });
        using var manager = await fixture.ManagerAsync();
        var doctor = await fixture.CreateDoctorAsync(manager);
        using var client = await fixture.LoginAsync(doctor.UserName);

        var read = await client.GetAsync($"/api/doctor/patients/{patient.Id}/medical-history");
        var dto = await Http.JsonAsync(read);
        Assert.Null(read.Headers.ETag);
        Assert.Equal(Fields.Concat(["patientId", "updatedAt"]).Order(), dto.EnumerateObject().Select(p => p.Name).Order());
        Assert.True(dto.GetProperty("hasDiabetes").GetBoolean());

        await Http.ProblemAsync(await client.PutAsync($"/api/sales/patients/{patient.Id}/medical-history", new { hasDiabetes = false }, "\"1\""), HttpStatusCode.Forbidden);
        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Post, HttpMethod.Patch })
        {
            var attempt = await client.SendAsync(method, $"/api/doctor/patients/{patient.Id}/medical-history", "{\"hasDiabetes\":false}");
            Assert.False(attempt.IsSuccessStatusCode);
        }

        Assert.Contains("patients.medical_history.read|DOCTOR", await fixture.Database.ColumnAsync(
            $"SELECT action || '|' || actor_role FROM audit.audit_log WHERE entity_id = '{patient.Id}'"));
    }

    [Fact] // AC-MED-002 / AC-MED-006: audit stores changed field NAMES only, never values
    public async Task Audit_stores_changed_field_names_only()
    {
        var (sales, patient) = await SalesWithPatientAsync();
        const string canary = "MED-CANARY-9d1e";
        var created = await sales.PutAsync($"/api/sales/patients/{patient.Id}/medical-history",
            new { hasHeartProblems = true, lastDentalVisit = canary });
        await sales.PutAsync($"/api/sales/patients/{patient.Id}/medical-history",
            new { hasHeartProblems = false, lastDentalVisit = canary, isSmoker = true }, created.Headers.ETag!.Tag);

        var rows = await fixture.Database.ColumnAsync(
            $"SELECT action || '|' || coalesce(new_value::text, '') || '|' || coalesce(previous_value::text, '') FROM audit.audit_log WHERE entity_type = 'patients.patient_medical_histories' AND entity_id = '{patient.Id}'");
        Assert.Contains("patients.medical_history.created|{\"changedFields\": [\"hasHeartProblems\", \"lastDentalVisit\"]}|", rows);
        Assert.Contains("patients.medical_history.updated|{\"changedFields\": [\"hasHeartProblems\", \"isSmoker\"]}|", rows);
        Assert.Equal(2, rows.Count);

        var all = await fixture.Database.ColumnAsync("SELECT coalesce(new_value::text,'') || coalesce(previous_value::text,'') FROM audit.audit_log");
        Assert.DoesNotContain(all, r => r.Contains(canary, StringComparison.Ordinal));
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>(
            "SELECT count(*) FROM audit.audit_log WHERE entity_type = 'patients.patient_medical_histories' AND action LIKE 'entity.%'"));
    }

    [Fact] // AC-MED-007 (current scope): the intake is independent of registration and of any appointment
    public async Task Patient_can_exist_and_be_viewed_without_an_intake()
    {
        var (sales, patient) = await SalesWithPatientAsync();
        await Http.ExpectAsync(await sales.GetAsync($"/api/sales/patients/{patient.Id}"), HttpStatusCode.OK);
        await Http.ProblemAsync(await sales.GetAsync($"/api/sales/patients/{patient.Id}/medical-history"), HttpStatusCode.NotFound);
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM patients.patient_medical_histories WHERE patient_id = '{patient.Id}'"));
    }

    [Fact] // AC-MED-008: holding the intake keys grants Sales no other clinical access
    public async Task Intake_permission_gives_sales_no_clinical_access()
    {
        var (sales, patient) = await SalesWithPatientAsync();

        await Http.ProblemAsync(await sales.GetAsync($"/api/doctor/patients/{patient.Id}"), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await sales.PutAsync($"/api/doctor/patients/{patient.Id}/clinical-profile", new { chiefComplaint = "x" }, "\"1\""), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await sales.PostAsync("/api/doctor/patients/search", new { query = "a" }), HttpStatusCode.Forbidden);

        var me = await Http.JsonAsync(await sales.GetAsync("/api/me"));
        var keys = me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToList();
        Assert.Contains("Patients.MedicalHistory.EditIntake", keys);
        Assert.DoesNotContain(keys, k => k is "Patients.Clinical.Read" or "Patients.Clinical.Write" or "Patients.Search.Clinical"
                                         || k.StartsWith("Clinical.", StringComparison.Ordinal) || k.StartsWith("Prescriptions.", StringComparison.Ordinal)
                                         || k.StartsWith("Staff.", StringComparison.Ordinal) || k.StartsWith("Finance.", StringComparison.Ordinal));
    }

    [Fact] // AC-MED-001 (d): one intake per patient; a row needs an existing patient; app cannot delete
    public async Task Intake_database_rules()
    {
        var (sales, patient) = await SalesWithPatientAsync();
        await sales.PutAsync($"/api/sales/patients/{patient.Id}/medical-history", new { hasAllergy = true });

        Assert.Equal("23505", await fixture.Database.TryExecuteAsync(
            $"INSERT INTO patients.patient_medical_histories (patient_id, created_at, updated_at) VALUES ('{patient.Id}', now(), now())"));
        Assert.Equal("23503", await fixture.Database.TryExecuteAsync(
            $"INSERT INTO patients.patient_medical_histories (patient_id, created_at, updated_at) VALUES ('{Guid.NewGuid()}', now(), now())"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("DELETE FROM patients.patient_medical_histories", "clinic_app"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("UPDATE patients.patient_medical_histories SET created_at = now()", "clinic_app"));
        // varchar(200) rejects first (22001); the CHECK (23514) is the second line of defense.
        Assert.Contains(await fixture.Database.TryExecuteAsync(
            $"UPDATE patients.patient_medical_histories SET last_dental_visit = repeat('x', 201) WHERE patient_id = '{patient.Id}'"), new[] { "22001", "23514" });
    }

    [Fact] // AC-MED-006: the Worker role cannot read the intake; the backup role can read but not write
    public async Task Worker_cannot_read_the_intake()
    {
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("SELECT 1 FROM patients.patient_medical_histories LIMIT 1", "clinic_worker"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("SELECT 1 FROM patients.patient_contacts LIMIT 1", "clinic_worker"));
        Assert.Null(await fixture.Database.TryExecuteAsync("SELECT 1 FROM patients.patient_medical_histories LIMIT 1", "clinic_backup"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("UPDATE patients.patient_medical_histories SET is_smoker = true", "clinic_backup"));
    }

    private async Task<(ApiClient Client, TestPatient Patient)> SalesWithPatientAsync()
    {
        using var manager = await fixture.ManagerAsync();
        var sales = await fixture.CreateSalesAsync(manager);
        var client = await fixture.LoginAsync(sales.UserName);
        return (client, await PatientFixtures.RegisterAsync(client));
    }
}
