using System.Globalization;
using System.Text.Json;

namespace Clinic.DemoSeed;

/// <summary>
/// Orthodontic cases through the real flow: the Doctor enters Gross Total, Deposit and Months; Sales may discount; the Visit's
/// finalization generates the schedule; payments are allocated to the Charge and applied to installments manually by Sales;
/// the Manager can raise the Gross Total (a PriceChange installment is added).
/// </summary>
internal static class OrthoStories
{
    private sealed class Run
    {
        public Guid CaseId;
        public Guid VisitId;
    }

    public static void Register(Plan plan, World w, int maxAgo)
    {
        // A: a progressed plan — deposit and the first two installments paid, later a Manager gross increase (PriceChange installment).
        var a = Start(plan, w, patient: 15, ago: Math.Min(30, maxAgo - 2), area: "UpperArch", appliance: null, gross: "18000.00", deposit: "4000.00", months: 6,
            discount: "1000.00");
        PayInstallments(plan, w, a, 15, w.Time.Ago(Math.Min(30, maxAgo - 2), 12, 0).AddMinutes(20), installmentCount: 1, includeDeposit: true);
        PayInstallments(plan, w, a, 15, w.Time.Ago(Math.Min(16, maxAgo - 3), 17, 30), installmentCount: 1, includeDeposit: false, firstIndex: 1);
        plan.At(w.Time.Ago(Math.Min(9, maxAgo - 4), 13, 0), "ortho gross increase", async () =>
        {
            var view = await w.SalesSession("demo").GetAsync($"/api/sales/orthodontic-cases/{a.CaseId}");
            await w.Manager.PutAsync($"/api/manager/orthodontic-cases/{a.CaseId}/terms",
                new { grossTotalPrice = "21000.00", depositAmount = "4000.00", numberOfMonths = 6, reason = "إضافة جهاز تثبيت" }, view.Ver());
            w.Bump("ortho gross increases");
        });

        // B: the oldest plan — deposit paid, the first installment only partly paid, the next installment not paid (overdue).
        var b = Start(plan, w, patient: 27, ago: Math.Max(3, maxAgo - 3), area: "Appliance", appliance: "جهاز تقويم متحرك", gross: "12000.00", deposit: "3000.00",
            months: 4, discount: null);
        plan.At(w.Time.Ago(Math.Max(3, maxAgo - 3), 12, 30), "ortho B deposit and part of installment 1", async () =>
        {
            var view = await w.SalesSession("two").GetAsync($"/api/sales/orthodontic-cases/{b.CaseId}");
            var first = view.GetProperty("installments").EnumerateArray().First();
            var amount = 3000m + 1000m;
            var payment = await PayToCharge(w, "two", 27, b.VisitId, amount);
            await w.SalesSession("two").PostAsync($"/api/sales/orthodontic-cases/{b.CaseId}/installment-payments",
                new { applications = new[] { new { allocationId = payment, installmentId = first.Id(), amount = Json.Money(1000m) } } });
        });

        // C: a brand-new plan — only the deposit paid, everything else still ahead.
        var c = Start(plan, w, patient: 19, ago: 2, area: "UpperArch", appliance: null, gross: "15000.00", deposit: "3500.00", months: 5, discount: "500.00");
        plan.At(w.Time.Ago(2, 12, 30), "ortho C deposit", async () => await PayToCharge(w, "demo", 19, c.VisitId, 3500m));
    }

    private static Run Start(Plan plan, World w, int patient, int ago, string area, string? appliance, string gross, string deposit, int months, string? discount)
    {
        var run = new Run();
        var t = w.Time.Ago(ago, 11, 0).AddMinutes(patient);
        var sales = PatientStories.SalesFor(patient);
        w.NeedPatient(patient, t.AddMinutes(-45));
        plan.At(t, $"ortho start {patient}", async () =>
        {
            var visit = await w.DoctorSession("ortho").PostAsync("/api/doctor/visits", new { patientId = w.Patients[patient].Id, appointmentId = (Guid?)null });
            run.VisitId = visit.Id();
        });
        plan.At(t.AddMinutes(4), $"ortho case {patient}", async () =>
        {
            var created = await w.DoctorSession("ortho").PostAsync($"/api/doctor/visits/{run.VisitId}/orthodontic-cases", new
            {
                area, applianceType = appliance, grossTotalPrice = gross, depositAmount = deposit, numberOfMonths = months,
            });
            run.CaseId = created.GuidOf("treatmentCaseId");
        });
        if (discount is not null)
        {
            plan.At(t.AddMinutes(7), $"ortho discount {patient}", async () =>
            {
                var visit = await w.DoctorSession("ortho").GetAsync($"/api/doctor/visits/{run.VisitId}");
                var line = visit.GetProperty("treatments").EnumerateArray().Last().GetProperty("ownPricing").Str("lineId");
                await w.SalesSession(sales).PutAsync($"/api/sales/pricing-lines/{line}/discount", new { discount, expectedEntrySeq = 0 });
            });
        }

        plan.At(t.AddMinutes(10), $"ortho notes {patient}", async () =>
        {
            var current = await w.DoctorSession("ortho").GetAsync($"/api/doctor/visits/{run.VisitId}");
            await w.DoctorSession("ortho").PutAsync($"/api/doctor/visits/{run.VisitId}",
                new { clinicalNotes = "تقييم أولي وتركيب الجهاز وشرح تعليمات العناية والمتابعة الشهرية.", consultationFee = (string?)null }, current.Ver());
        });
        plan.At(t.AddMinutes(25), $"ortho finalize {patient}", async () =>
        {
            var current = await w.DoctorSession("ortho").GetAsync($"/api/doctor/visits/{run.VisitId}");
            await w.DoctorSession("ortho").PostAsync($"/api/doctor/visits/{run.VisitId}/finalize", null, current.Ver());
            w.Bump("visits finalized");
            w.Bump("orthodontic cases");
        });
        return run;
    }

    /// <summary>Pays (deposit +) the next installments and applies each payment to its installment.</summary>
    private static void PayInstallments(Plan plan, World w, Run run, int patient, DateTimeOffset at, int installmentCount, bool includeDeposit, int firstIndex = 0)
    {
        var sales = PatientStories.SalesFor(patient);
        plan.At(at, $"ortho payments {patient}", async () =>
        {
            var view = await w.SalesSession(sales).GetAsync($"/api/sales/orthodontic-cases/{run.CaseId}");
            var installments = view.GetProperty("installments").EnumerateArray().Skip(firstIndex).Take(installmentCount).ToList();
            var deposit = includeDeposit ? decimal.Parse(view.GetProperty("terms").Str("depositAmount"), CultureInfo.InvariantCulture) : 0m;
            var total = deposit + installments.Sum(i => decimal.Parse(i.Str("amount"), CultureInfo.InvariantCulture));
            var allocationId = await PayToCharge(w, sales, patient, run.VisitId, total);
            foreach (var installment in installments)
            {
                await w.SalesSession(sales).PostAsync($"/api/sales/orthodontic-cases/{run.CaseId}/installment-payments",
                    new { applications = new[] { new { allocationId, installmentId = installment.Id(), amount = installment.Str("amount") } } });
            }
        });
    }

    /// <summary>Pays <paramref name="amount"/> against the Visit's charge and returns the id of the allocation to the charge.</summary>
    private static async Task<Guid> PayToCharge(World w, string salesKey, int patient, Guid visitId, decimal amount)
    {
        var sales = w.SalesSession(salesKey);
        var balance = await sales.GetAsync($"/api/sales/patients/{w.Patients[patient].Id}/balance");
        var charge = balance.GetProperty("charges").EnumerateArray().First(c => c.GuidOf("visitId") == visitId);
        var payment = await sales.PostKeyedAsync("/api/sales/payments", new
        {
            patientId = w.Patients[patient].Id, amount = Json.Money(amount), method = "Cash",
            allocations = new[] { new { chargeId = charge.Id(), amount = Json.Money(amount) } },
        });
        w.Bump("payments");
        return payment.GetProperty("allocations").EnumerateArray().Single().GuidOf("allocationId");
    }
}
