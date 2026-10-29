using System.Text.Json;

namespace Clinic.DemoSeed;

/// <summary>Clinic settings, payment/expense lookups, lab percentage, the staff (8 Doctors, 3 Sales / Reception, 4 Assistants) and availability.</summary>
internal static class Foundation
{
    public const string ClinicName = "عيادة النور لطب الأسنان";
    public const string PartialMarker = " (قيد تجهيز البيانات التجريبية)";
    public const string CompleteMarker = " (بيانات تجريبية)";

    private sealed record DoctorSpec(string Key, string UserName, string FullName, string Specialty, string Percentage);

    private static readonly DoctorSpec[] DoctorSpecs =
    [
        new("surgeon", "dr.hisham", "د. هشام عبد الرحمن", "جراحة الفم والأسنان", "45.00"),
        new("ortho", "dr.mona", "د. منى السيد", "تقويم الأسنان", "40.00"),
        new("demo", "doctor.demo", "د. كريم فؤاد", "طب أسنان عام", "35.00"),
        new("gp2", "dr.sara", "د. سارة الشافعي", "طب أسنان عام", "30.00"),
        new("gp3", "dr.ahmed", "د. أحمد النجار", "طب أسنان عام", "35.00"),
        new("gp4", "dr.yasmin", "د. ياسمين حسن", "طب أسنان عام", "30.00"),
        new("gp5", "dr.amr", "د. عمرو ممدوح", "طب أسنان عام", "30.00"),
        new("gp6", "dr.nourhan", "د. نورهان يوسف", "طب أسنان عام", "32.50"),
    ];

    private sealed record SalesSpec(string Key, string UserName, string FullName, string Salary, string Commission);

    private static readonly SalesSpec[] SalesSpecs =
    [
        new("demo", "sales.demo", "ريم علاء", "7000.00", "5.00"),
        new("two", "sales.mohamed", "محمد سمير", "6500.00", "4.00"),
        new("three", "sales.heba", "هبة جمال", "6000.00", "4.50"),
    ];

    private static readonly (string Name, string Salary, string? OvertimeRate)[] AssistantSpecs =
    [
        ("أمل فتحي", "5200.00", "40.00"), ("سعاد رمضان", "5000.00", "38.00"), ("هدير عادل", "4800.00", "35.00"), ("منة الله سيد", "4600.00", null),
    ];

    public static void Register(Plan plan, World w)
    {
        var t0 = w.Time.At(w.Time.FirstOfMonth(1), 8, 0);

        plan.At(t0, "settings", async () =>
        {
            var (current, etag) = await w.Manager.GetVersionedAsync("/api/manager/settings");
            await w.Manager.PutVersionedAsync("/api/manager/settings", new
            {
                clinicTimezone = current.Str("clinicTimezone"),
                doctorDobVisible = current.GetProperty("doctorDobVisible").GetBoolean(),
                clinicName = ClinicName + PartialMarker,
                clinicContact = "القاهرة — هاتف 0200000000 — بيانات تجريبية",
            }, etag);
        });

        plan.At(t0.AddMinutes(2), "lookups", async () =>
        {
            foreach (var method in (await w.Manager.GetAsync("/api/manager/payment-methods")).Items())
            {
                w.PaymentMethodCodes[method.Str("code")] = method.Str("code");
            }

            foreach (var category in (await w.Manager.GetAsync("/api/manager/expense-categories")).Items())
            {
                w.ExpenseCategories[category.Str("name")] = category.Id();
            }
        });

        plan.At(t0.AddMinutes(3), "lab percentage", async () =>
            await w.Manager.PutAsync("/api/manager/settings/lab-percentage", new { percentage = "12.00", reason = "النسبة الافتتاحية" }));

        var minute = 5;
        foreach (var spec in DoctorSpecs)
        {
            plan.At(t0.AddMinutes(minute++), $"doctor {spec.UserName}", async () =>
            {
                var created = await w.Manager.PostAsync("/api/manager/staff/doctors", new
                {
                    fullName = spec.FullName, specialty = spec.Specialty, currentPercentage = spec.Percentage, userName = spec.UserName,
                    initialPassword = w.Password,
                });
                w.Doctors[spec.Key] = new StaffRef(created.Id(), created.GuidOf("userId"), spec.UserName, spec.FullName);
            });
        }

        foreach (var spec in SalesSpecs)
        {
            plan.At(t0.AddMinutes(minute++), $"sales {spec.UserName}", async () =>
            {
                var created = await w.Manager.PostAsync("/api/manager/staff/sales-employees", new
                {
                    fullName = spec.FullName, baseMonthlySalary = spec.Salary, commissionPercentage = spec.Commission, userName = spec.UserName,
                    initialPassword = w.Password,
                });
                w.Sales[spec.Key] = new StaffRef(created.Id(), created.GuidOf("userId"), spec.UserName, spec.FullName);
            });
        }

        foreach (var (name, salary, rate) in AssistantSpecs)
        {
            plan.At(t0.AddMinutes(minute++), $"assistant {name}", async () =>
            {
                var created = await w.Manager.PostAsync("/api/manager/staff/assistants", new { fullName = name, currentSalary = salary });
                if (rate is not null)
                {
                    await w.Manager.PutAsync($"/api/manager/staff/assistants/{created.Id()}/overtime-rate",
                        new { overtimeHourlyRate = rate, reason = "سعر الساعة الإضافية" }, created.Ver());
                }

                w.Assistants[name] = created.Id();
            });
        }
    }

    /// <summary>Two published availability weeks (starting today and a week later) for every Doctor, working days only.</summary>
    public static void RegisterAvailability(Plan plan, World w)
    {
        plan.At(w.Time.At(w.Time.Today, 7, 30), "availability", async () =>
        {
            foreach (var (key, doctor) in w.Doctors)
            {
                // The Orthodontist and the Surgeon work shorter, different sessions; General Practitioners work two shifts.
                foreach (var weekStart in new[] { w.Time.Today, w.Time.Today.AddDays(7) })
                {
                    var ranges = new List<object>();
                    for (var d = 0; d < 7; d++)
                    {
                        var date = weekStart.AddDays(d);
                        if (date.DayOfWeek == DayOfWeek.Friday)
                        {
                            continue;
                        }

                        if (key == "surgeon" && date.DayOfWeek is DayOfWeek.Sunday or DayOfWeek.Tuesday or DayOfWeek.Thursday)
                        {
                            ranges.Add(new { date, start = "12:00", end = "20:00" });
                        }
                        else if (key == "ortho")
                        {
                            ranges.Add(new { date, start = "14:00", end = "21:00" });
                        }
                        else if (key != "surgeon")
                        {
                            ranges.Add(new { date, start = "09:00", end = "13:00" });
                            ranges.Add(new { date, start = "16:00", end = "20:00" });
                        }
                    }

                    var url = $"/api/manager/doctors/{doctor.StaffId}/availability/{weekStart:yyyy-MM-dd}";
                    var draft = await w.Manager.PutAsync(url, new { ranges });
                    await w.Manager.PostAsync(url + "/publish", null, draft.Ver());
                }
            }
        });
    }

    public static void RegisterCompletionMarker(Plan plan, World w, DateTimeOffset at)
    {
        plan.At(at, "completion marker", async () =>
        {
            var (current, etag) = await w.Manager.GetVersionedAsync("/api/manager/settings");
            await w.Manager.PutVersionedAsync("/api/manager/settings", new
            {
                clinicTimezone = current.Str("clinicTimezone"),
                doctorDobVisible = current.GetProperty("doctorDobVisible").GetBoolean(),
                clinicName = ClinicName + CompleteMarker,
                clinicContact = current.Str("clinicContact"),
            }, etag);
        });
    }
}
