using System.Text.Json;

namespace Clinic.DemoSeed;

/// <summary>Patient registration (Walk-In and Lead-sourced), the structured Medical History intake, leads and follow-ups.</summary>
internal static class PatientStories
{
    /// <summary>Patients who came from a Lead (the rest are Walk-In). Value = the Sales key that owned the lead.</summary>
    public static readonly Dictionary<int, string> LeadSourced = new()
    {
        [4] = "demo", [9] = "two", [13] = "demo", [17] = "three", [21] = "two", [24] = "demo", [28] = "three", [33] = "two", [36] = "demo", [41] = "three",
    };

    private static readonly string[] SalesCycle = ["demo", "two", "three"];

    public static string SalesFor(int patientIndex) => LeadSourced.TryGetValue(patientIndex, out var key) ? key : SalesCycle[patientIndex % 3];

    /// <summary>Registers (or converts a lead into) the patient at <paramref name="at"/>, plus the intake where the profile has one.</summary>
    public static void Register(Plan plan, World w, int index, DateTimeOffset at)
    {
        var person = Cast.Patient(index);
        var salesKey = SalesFor(index);
        object Body() => new
        {
            fullName = person.Name,
            dateOfBirth = $"{person.BirthYear}-{1 + (index * 5) % 12:00}-{1 + (index * 7) % 27:00}",
            phone = person.Phone,
            whatsapp = index % 3 == 0 ? person.Phone : null,
            email = index % 4 == 1 ? $"patient{index + 1}@example.com" : null,
            address = person.Address,
            crmNotes = index % 5 == 0 ? $"يفضل الحجز مساءً — {Cast.Marker}" : Cast.Marker,
        };

        if (LeadSourced.ContainsKey(index))
        {
            // The lead existed before; it is converted now (conversion sets the lead to Converted and the patient's origin to Lead).
            plan.At(at.AddMinutes(-90), $"lead for patient {index}", async () =>
            {
                var lead = await w.SalesSession(salesKey).PostAsync("/api/sales/leads", new
                {
                    name = person.Name, phone = person.Phone, source = Cast.LeadSources[index % Cast.LeadSources.Length],
                    interestedService = Cast.LeadServices[index % Cast.LeadServices.Length], notes = "تم الاتصال وحجز موعد الكشف", nextFollowUpDate = (string?)null,
                    status = "Booked", salesEmployeeId = (Guid?)null,
                });
                w.Facts[$"lead:{index}"] = lead.Id();
            });
            plan.At(at, $"convert lead {index}", async () =>
            {
                var current = await w.SalesSession(salesKey).GetAsync($"/api/sales/leads/{w.Facts[$"lead:{index}"]}");
                var converted = await w.SalesSession(salesKey).PostAsync($"/api/sales/leads/{current.Id()}/convert", Body(), current.Ver());
                w.Patients[index] = new PatientRef(converted.GuidOf("patientId"), "", person.Name, index);
                w.Bump("patients (from leads)");
            });
        }
        else
        {
            plan.At(at, $"register patient {index}", async () =>
            {
                var created = await w.SalesSession(salesKey).PostAsync("/api/sales/patients", Body());
                w.Patients[index] = new PatientRef(created.Id(), created.Str("patientCode"), person.Name, index);
                w.Bump("patients (walk-in)");
            });
        }

        var chosen = IntakeFor(index);
        if (chosen is not null)
        {
            plan.At(at.AddMinutes(15), $"medical intake {index}", async () =>
            {
                await w.SalesSession(salesKey).PutAsync($"/api/sales/patients/{w.Patients[index].Id}/medical-history", chosen);
                w.Bump("medical history intakes");
            });
        }
    }

    /// <summary>Pregnancy / breastfeeding answers are given only for adult women; most patients have a general intake, some none (booking never depends on it).</summary>
    private static object? IntakeFor(int index) => index switch
    {
        25 or 34 => MedicalProfiles[4],
        19 or 38 => MedicalProfiles[6],
        _ when index % 2 == 0 || index % 7 == 3 => MedicalProfiles[index % 6],
        _ => null,
    };

    /// <summary>Structured intake answers only (the confirmed fields). Null = not answered.</summary>
    private static readonly Dictionary<int, object> MedicalProfiles = new()
    {
        [0] = new { hasAllergy = true, hasHypertension = false, hasViralHepatitis = false, hasDiabetes = false, hasKidneyProblems = false, hasBleedingOrClottingProblem = false, hasHeartProblems = false, hasRheumaticDisease = false, isSmoker = false, hasPreviousSurgicalOperations = false, isTakingMedications = false, lastDentalVisit = "منذ ستة أشهر", isPregnant = (bool?)null, isBreastfeeding = (bool?)null },
        [1] = new { hasAllergy = false, hasHypertension = true, hasViralHepatitis = false, hasDiabetes = true, hasKidneyProblems = false, hasBleedingOrClottingProblem = false, hasHeartProblems = false, hasRheumaticDisease = false, isSmoker = true, hasPreviousSurgicalOperations = true, isTakingMedications = true, lastDentalVisit = "منذ سنتين", isPregnant = (bool?)null, isBreastfeeding = (bool?)null },
        [2] = new { hasAllergy = false, hasHypertension = false, hasViralHepatitis = true, hasDiabetes = false, hasKidneyProblems = false, hasBleedingOrClottingProblem = true, hasHeartProblems = false, hasRheumaticDisease = false, isSmoker = false, hasPreviousSurgicalOperations = false, isTakingMedications = true, lastDentalVisit = "لم يسبق", isPregnant = (bool?)null, isBreastfeeding = (bool?)null },
        [3] = new { hasAllergy = false, hasHypertension = false, hasViralHepatitis = false, hasDiabetes = false, hasKidneyProblems = true, hasBleedingOrClottingProblem = false, hasHeartProblems = true, hasRheumaticDisease = false, isSmoker = false, hasPreviousSurgicalOperations = true, isTakingMedications = true, lastDentalVisit = "منذ سنة", isPregnant = (bool?)null, isBreastfeeding = (bool?)null },
        [4] = new { hasAllergy = true, hasHypertension = false, hasViralHepatitis = false, hasDiabetes = false, hasKidneyProblems = false, hasBleedingOrClottingProblem = false, hasHeartProblems = false, hasRheumaticDisease = true, isSmoker = false, hasPreviousSurgicalOperations = false, isTakingMedications = true, lastDentalVisit = "منذ ثلاثة أشهر", isPregnant = true, isBreastfeeding = false },
        [5] = new { hasAllergy = (bool?)null, hasHypertension = (bool?)null, hasViralHepatitis = (bool?)null, hasDiabetes = true, hasKidneyProblems = (bool?)null, hasBleedingOrClottingProblem = (bool?)null, hasHeartProblems = (bool?)null, hasRheumaticDisease = (bool?)null, isSmoker = true, hasPreviousSurgicalOperations = (bool?)null, isTakingMedications = (bool?)null, lastDentalVisit = (string?)null, isPregnant = (bool?)null, isBreastfeeding = (bool?)null },
        [6] = new { hasAllergy = false, hasHypertension = false, hasViralHepatitis = false, hasDiabetes = false, hasKidneyProblems = false, hasBleedingOrClottingProblem = false, hasHeartProblems = false, hasRheumaticDisease = false, isSmoker = false, hasPreviousSurgicalOperations = false, isTakingMedications = false, lastDentalVisit = "منذ شهر", isPregnant = false, isBreastfeeding = true },
    };

    /// <summary>The Sales-side work around leads: leads in every status, follow-ups in every status and type.</summary>
    public static void RegisterLeadsAndFollowUps(Plan plan, World w)
    {
        // Leads that never became patients, one or more per status (Converted ones come from the patient stories above).
        var statuses = new[]
        {
            "New", "New", "New", "Contacted", "Contacted", "Interested", "CallLater", "NoAnswer", "NoAnswer", "NotInterested", "Booked", "Booked",
        };
        foreach (var i in Enumerable.Range(0, statuses.Length))
        {
            var (name, phone) = Cast.Lead(i);
            var status = statuses[i];
            var salesKey = new[] { "demo", "two", "three" }[i % 3];
            var ago = 14 - i;
            var at = w.Time.Ago(Math.Max(1, ago), 11, 10 + i);
            var notes = status switch
            {
                "NoAnswer" => "لم يرد على الاتصال مرتين",
                "CallLater" => "طلب الاتصال بعد نهاية الأسبوع",
                "NotInterested" => "السعر أعلى من المتوقع",
                "Interested" => "يرغب في معرفة تكلفة الزراعة",
                _ => "عميل محتمل جديد من الحملة الإعلانية",
            };
            plan.At(at, $"lead {i} {status}", async () =>
            {
                var created = await w.SalesSession(salesKey).PostAsync("/api/sales/leads", new
                {
                    name, phone, source = Cast.LeadSources[i % Cast.LeadSources.Length], interestedService = Cast.LeadServices[(i + 2) % Cast.LeadServices.Length],
                    notes, nextFollowUpDate = w.Time.WorkdaysAhead(1 + i % 4).ToString("yyyy-MM-dd"), status, salesEmployeeId = (Guid?)null,
                });
                w.Facts[$"openlead:{i}"] = created.Id();
                w.Bump("leads (open and closed)");
            });
        }

        // Follow-ups: every status (New, Contacted, NoResponse, FollowedUp) and every type, owned by active Sales.
        var types = new[] { "Lead", "GeneralTreatmentFollowUp", "RemainingBalance", "DoctorReferral", "ImplantAppointment", "OrthoInstallment" };
        var fuStatuses = new[] { "New", "Contacted", "NoResponse", "FollowedUp" };
        foreach (var i in Enumerable.Range(0, 12))
        {
            var type = types[i % types.Length];
            var status = fuStatuses[i % fuStatuses.Length];
            var salesKey = new[] { "demo", "two", "three" }[i % 3];
            var patientIndex = i;
            plan.At(w.Time.Ago(Math.Max(1, 9 - i / 2), 15, 5 + i), $"follow-up {i}", async () =>
            {
                await w.SalesSession(salesKey).PostAsync("/api/sales/follow-ups", new
                {
                    followUpType = type,
                    patientId = type == "Lead" ? (Guid?)null : w.Patients[patientIndex].Id,
                    leadId = type == "Lead" ? w.Facts[$"openlead:{i}"] : (Guid?)null,
                    dueDate = (i % 4 == 0 ? w.Time.WorkdaysAgo(1) : w.Time.WorkdaysAhead(1 + i % 5)).ToString("yyyy-MM-dd"),
                    status,
                    notes = type switch
                    {
                        "RemainingBalance" => "تذكير بسداد المتبقي",
                        "ImplantAppointment" => "تأكيد موعد متابعة الزراعة",
                        "OrthoInstallment" => "تذكير بموعد القسط",
                        _ => "متابعة بعد الزيارة",
                    },
                    result = status == "FollowedUp" ? "تمت المتابعة وتم الاتفاق على الموعد" : null,
                    nextFollowUpDate = (string?)null,
                    salesEmployeeId = (Guid?)null,
                });
                w.Bump($"follow-ups {status}");
            });
        }
    }
}
