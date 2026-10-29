using System.Text.Json;

namespace Clinic.DemoSeed;

/// <summary>
/// One patient encounter, written as plan steps at explicit times: book → (confirm) → arrive → start → treat → price →
/// (discount, fee, fixed lab) → finalize → pay. Every call goes through the same API a user's browser would use.
/// </summary>
internal sealed class VisitScript
{
    private readonly Plan plan;
    private readonly World w;
    private readonly string doctorKey;
    private readonly string salesKey;
    private readonly List<(Guid RecordId, Guid CaseId, string LineId)> records = [];

    public VisitScript(Plan plan, World w, int patientIndex, string doctorKey, string salesKey)
    {
        this.plan = plan;
        this.w = w;
        PatientIndex = patientIndex;
        this.doctorKey = doctorKey;
        this.salesKey = salesKey;
    }

    public int PatientIndex { get; }
    public JsonElement Appointment { get; private set; }
    public JsonElement Visit { get; private set; }
    public Guid? ChargeId { get; private set; }

    private StaffRef doctor => w.Doctor(doctorKey);
    private PatientRef Patient => w.Patients[PatientIndex];
    private ApiSession Sales => w.SalesSession(salesKey);
    private ApiSession Doc => w.As(doctor);

    public VisitScript Book(DateTimeOffset at, DateTimeOffset start, int minutes, string reason, string? kind = null, string? notes = null)
    {
        plan.At(at, $"book {PatientIndex}", async () =>
            Appointment = await Sales.PostAsync("/api/sales/appointments", new
            {
                patientId = Patient.Id, doctorId = doctor.StaffId, startAt = start, durationMinutes = minutes, reason, salesNotes = notes, kind,
            }));
        return this;
    }

    public VisitScript Confirm(DateTimeOffset at)
    {
        plan.At(at, $"confirm {PatientIndex}", async () =>
            Appointment = await Sales.PostAsync($"/api/sales/appointments/{Appointment.Id()}/status",
                new { status = "Confirmed", reason = (string?)null }, Appointment.Ver()));
        return this;
    }

    /// <summary>A terminal status chosen by Sales: Cancelled needs a reason, NoShow does not.</summary>
    public VisitScript SetStatus(DateTimeOffset at, string status, string? reason = null)
    {
        plan.At(at, $"{status} {PatientIndex}", async () =>
            Appointment = await Sales.PostAsync($"/api/sales/appointments/{Appointment.Id()}/status", new { status, reason }, Appointment.Ver()));
        return this;
    }

    public VisitScript Reschedule(DateTimeOffset at, DateTimeOffset newStart, int minutes, string reason)
    {
        plan.At(at, $"reschedule {PatientIndex}", async () =>
            Appointment = await Sales.PostAsync($"/api/sales/appointments/{Appointment.Id()}/reschedule",
                new { startAt = newStart, durationMinutes = minutes, rescheduleReason = reason }, Appointment.Ver()));
        return this;
    }

    public VisitScript Arrive(DateTimeOffset at)
    {
        plan.At(at, $"arrive {PatientIndex}", async () =>
            await Sales.PostAsync($"/api/sales/appointments/{Appointment.Id()}/arrival"));
        return this;
    }

    /// <summary>The Doctor starts the Visit; from the arrived appointment when there is one.</summary>
    public VisitScript Start(DateTimeOffset at, bool fromAppointment = true)
    {
        plan.At(at, $"start {PatientIndex}", async () =>
            Visit = await Doc.PostAsync("/api/doctor/visits", new
            {
                patientId = Patient.Id, appointmentId = fromAppointment ? Appointment.Id() : (Guid?)null,
            }));
        return this;
    }

    public VisitScript Notes(DateTimeOffset at, string clinicalNotes, string? consultationFee = null)
    {
        plan.At(at, $"notes {PatientIndex}", async () =>
        {
            var current = await Doc.GetAsync($"/api/doctor/visits/{Visit.Id()}");
            Visit = await Doc.PutAsync($"/api/doctor/visits/{Visit.Id()}", new { clinicalNotes, consultationFee }, current.Ver());
        });
        return this;
    }

    /// <summary>Adds a catalog treatment and prices it (the Doctor's own work price).</summary>
    public VisitScript Treat(DateTimeOffset at, string procedureCode, string price, string[]? teeth = null, object? details = null)
    {
        plan.At(at, $"treat {PatientIndex} {procedureCode}", async () =>
        {
            var visit = await Doc.PostAsync($"/api/doctor/visits/{Visit.Id()}/treatments", new { procedureCode, teeth, details });
            var treatment = visit.GetProperty("treatments").EnumerateArray().Last();
            var recordId = treatment.GuidOf("recordId");
            var caseId = treatment.GetProperty("case").Id();
            await Doc.PutAsync($"/api/doctor/treatment-records/{recordId}/pricing", new { price });
            var priced = await Doc.GetAsync($"/api/doctor/visits/{Visit.Id()}");
            var line = priced.GetProperty("treatments").EnumerateArray().Last().GetProperty("ownPricing");
            records.Add((recordId, caseId, line.Str("lineId")));
            Visit = priced;
        });
        return this;
    }

    /// <summary>Discount (fixed EGP) on the last treatment line, by the Doctor or by Sales. Allowed only before the Charge exists.</summary>
    public VisitScript Discount(DateTimeOffset at, string amount, bool bySales = false)
    {
        plan.At(at, $"discount {PatientIndex}", async () =>
        {
            var line = records[^1].LineId;
            var url = bySales ? $"/api/sales/pricing-lines/{line}/discount" : $"/api/doctor/pricing-lines/{line}/discount";
            await (bySales ? Sales : Doc).PutAsync(url, new { discount = amount, expectedEntrySeq = 0 });
        });
        return this;
    }

    public VisitScript FixedLab(DateTimeOffset at, string amount)
    {
        plan.At(at, $"fixed lab {PatientIndex}", async () =>
            await Doc.PostAsync($"/api/doctor/fixed-lab/{records[^1].CaseId}", new { amount }));
        return this;
    }

    public VisitScript Finalize(DateTimeOffset at)
    {
        plan.At(at, $"finalize {PatientIndex}", async () =>
        {
            var current = await Doc.GetAsync($"/api/doctor/visits/{Visit.Id()}");
            Visit = await Doc.PostAsync($"/api/doctor/visits/{Visit.Id()}/finalize", null, current.Ver());
            var balance = await Sales.GetAsync($"/api/sales/patients/{Patient.Id}/balance");
            var visitId = Visit.Id();
            ChargeId = balance.GetProperty("charges").EnumerateArray().FirstOrDefault(c => c.GuidOf("visitId") == visitId) is { ValueKind: JsonValueKind.Object } charge
                ? charge.Id() : null;
            w.Bump("visits finalized");
        });
        return this;
    }

    /// <summary>Records a payment of <paramref name="amount"/>; the part up to the Visit's outstanding is allocated, any excess becomes Patient Credit.</summary>
    public VisitScript Pay(DateTimeOffset at, decimal amount, string method = "Cash")
    {
        plan.At(at, $"pay {PatientIndex} {amount}", () => PayAsync(amount, method));
        return this;
    }

    /// <summary>Pays whatever is still outstanding on the Visit's charges at the time the step runs.</summary>
    public VisitScript PayAll(DateTimeOffset at, string method = "Cash")
    {
        plan.At(at, $"pay all {PatientIndex}", () => PayAsync(null, method));
        return this;
    }

    private async Task PayAsync(decimal? amount, string method)
    {
        var balance = await Sales.GetAsync($"/api/sales/patients/{Patient.Id}/balance");
        var visitId = Visit.Id();
        var open = balance.GetProperty("charges").EnumerateArray().Where(c => c.GuidOf("visitId") == visitId)
            .Select(c => (Id: c.Id(), Outstanding: decimal.Parse(c.Str("outstanding"), System.Globalization.CultureInfo.InvariantCulture)))
            .Where(c => c.Outstanding > 0).ToList();
        var pay = amount ?? open.Sum(c => c.Outstanding);
        if (pay <= 0)
        {
            return;
        }

        var left = pay;
        var allocations = new List<object>();
        foreach (var (id, outstanding) in open)
        {
            var take = Math.Min(left, outstanding);
            if (take > 0)
            {
                allocations.Add(new { chargeId = id, amount = Json.Money(take) });
                left -= take;
            }
        }

        await Sales.PostKeyedAsync("/api/sales/payments", new { patientId = Patient.Id, amount = Json.Money(pay), method, allocations });
        w.Bump("payments");
    }

    /// <summary>The Doctor records the patient's chief complaint (clinical profile; never visible to Sales).</summary>
    public VisitScript ChiefComplaint(DateTimeOffset at, string chiefComplaint)
    {
        plan.At(at, $"chief complaint {PatientIndex}", async () =>
        {
            var (_, etag) = await Doc.GetVersionedAsync($"/api/doctor/patients/{Patient.Id}");
            await Doc.PutVersionedAsync($"/api/doctor/patients/{Patient.Id}/clinical-profile", new { chiefComplaint }, etag);
        });
        return this;
    }

    /// <summary>Root canal working lengths for the last treatment (Doctor-only clinical data).</summary>
    public VisitScript Canals(DateTimeOffset at, params (int Number, string LengthMm)[] canals)
    {
        plan.At(at, $"canals {PatientIndex}", async () =>
            await Doc.PutAsync($"/api/doctor/visits/{Visit.Id()}/cases/{records[^1].CaseId}/canal-measurements",
                new { canals = canals.Select(c => new { canalNumber = c.Number, lengthMm = c.LengthMm }).ToArray() }));
        return this;
    }

    public VisitScript Step(DateTimeOffset at, string stepCode)
    {
        plan.At(at, $"step {stepCode} {PatientIndex}", async () =>
            await Doc.PostAsync($"/api/doctor/visits/{Visit.Id()}/cases/{records[^1].CaseId}/steps/{stepCode}/complete"));
        return this;
    }

    public VisitScript CaseStatus(DateTimeOffset at, string status)
    {
        plan.At(at, $"case {status} {PatientIndex}", async () =>
            await Doc.PutAsync($"/api/doctor/visits/{Visit.Id()}/cases/{records[^1].CaseId}/status", new { status }));
        return this;
    }

    public VisitScript Implant(DateTimeOffset at, bool boneGraft, bool membrane, string procedureDate, int healingMonths)
    {
        plan.At(at, $"implant details {PatientIndex}", async () =>
            await Doc.PutAsync($"/api/doctor/visits/{Visit.Id()}/cases/{records[^1].CaseId}/implant",
                new { hasBoneGraft = boneGraft, hasMembrane = membrane, procedureDate, healingMonths }));
        return this;
    }

    public VisitScript Prescribe(DateTimeOffset at, string generalNotes, params (string Name, string Dosage, string Frequency, string Duration)[] items)
    {
        plan.At(at, $"prescription {PatientIndex}", async () =>
        {
            await Doc.PostAsync($"/api/doctor/visits/{Visit.Id()}/prescriptions", new
            {
                generalNotes,
                items = items.Select(i => new { id = (Guid?)null, medicationName = i.Name, dosage = i.Dosage, frequency = i.Frequency, duration = i.Duration, instructions = (string?)null }).ToArray(),
            });
            w.Bump("prescriptions");
        });
        return this;
    }

    public VisitScript Referral(DateTimeOffset at, string referralType, string reason, string? notes, string? instructions)
    {
        plan.At(at, $"referral {PatientIndex}", async () =>
        {
            await Doc.PostAsync($"/api/doctor/visits/{Visit.Id()}/referrals", new { referralType, reason, notes, followUpInstructions = instructions });
            w.Bump("doctor referrals");
        });
        return this;
    }

    /// <summary>The Doctor books the follow-up appointment of an Implant case.</summary>
    public VisitScript ImplantFollowUp(DateTimeOffset at, DateTimeOffset start, int minutes)
    {
        plan.At(at, $"implant follow-up {PatientIndex}", async () =>
        {
            await Doc.PostAsync("/api/doctor/appointments/follow-up", new
            {
                visitId = Visit.Id(), startAt = start, durationMinutes = minutes, reason = "متابعة زراعة", kind = "ImplantFollowUp",
            });
            w.Bump("implant follow-up appointments");
        });
        return this;
    }

    public JsonElement LastTreatmentOf(JsonElement visit) => visit.GetProperty("treatments").EnumerateArray().Last();
}
