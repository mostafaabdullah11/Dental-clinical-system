using System.Text.Json;

namespace Clinic.DemoSeed;

/// <summary>
/// Fixed-Lab cases (Implant, Fixed / Removable Prosthetics) with Lab-first cash priority and a pending Doctor entitlement, a lab
/// percentage revision (General Lab), effective-dated commission / Doctor percentage revisions, Patient Credit (overpayment and
/// advance), a Manager refund, reversal and discount correction, and the Doctor / Sales deactivation scenarios.
/// </summary>
internal static class LabAndFinanceStories
{
    public static void Register(Plan plan, World w, int maxAgo)
    {
        DateTimeOffset Ago(int n, int h, int m = 0) => w.Time.Ago(Journeys.Scale(n, maxAgo), h, m);
        string DateAgo(int n) => w.Time.WorkdaysAgo(Journeys.Scale(n, maxAgo)).ToString("yyyy-MM-dd");

        // ——— Fixed Lab: Implant, lab invoice entered by the Doctor, patient pays less than the lab cost first ———
        var implant = new VisitScript(plan, w, 37, "surgeon", PatientStories.SalesFor(37));
        w.NeedPatient(37, Ago(18, 12));
        implant.Start(Ago(18, 13), fromAppointment: false)
            .Notes(Ago(18, 13, 5), "زراعة سن في منطقة الضرس السفلي. تم التخدير الموضعي وتحضير الموضع وتركيب الزرعة.")
            .Treat(Ago(18, 13, 10), "Implant", "16000.00", ["36"])
            .Implant(Ago(18, 13, 20), boneGraft: true, membrane: false, procedureDate: DateAgo(18), healingMonths: 4)
            .FixedLab(Ago(18, 13, 25), "4000.00")
            .Prescribe(Ago(18, 13, 28), "بعد الأكل", ("Augmentin 1g", "قرص", "مرتين يومياً", "7 أيام"), ("Brufen 400mg", "قرص", "عند اللزوم", "3 أيام"))
            .Finalize(Ago(18, 14, 15))
            .Pay(Ago(18, 14, 20), 3000m) // less than the lab cost: everything goes to the lab first, the Doctor's entitlement stays pending
            .Pay(Ago(10, 12, 30), 5000m)
            .PayAll(Ago(3, 15, 0), "InstaPay")
            .ImplantFollowUp(Ago(18, 14, 30), w.Time.Ahead(3, 14, 0), 30);
        plan.At(Ago(11, 9, 0), "Manager revises the lab invoice", async () =>
        {
            var caseId = await FirstCaseOfAsync(w, 37, "Implant");
            await w.Manager.PutAsync($"/api/manager/fixed-lab/{caseId}", new { amount = "4200.00", reason = "تعديل قيمة فاتورة المعمل" });
            w.Bump("fixed lab revisions");
        });

        var implant2 = new VisitScript(plan, w, 40, "surgeon", PatientStories.SalesFor(40));
        w.NeedPatient(40, Ago(13, 11));
        implant2.Book(Ago(13, 12, 20), Ago(12, 14, 0), 60, "زراعة").Confirm(Ago(13, 12, 40)).Arrive(Ago(12, 13, 50)).Start(Ago(12, 14, 3))
            .Notes(Ago(12, 14, 5), "زراعة سن علوي مع رفع جيب الجيب الأنفي المحدود.")
            .Treat(Ago(12, 14, 10), "Implant", "14000.00", ["14"])
            .Implant(Ago(12, 14, 25), boneGraft: false, membrane: true, procedureDate: DateAgo(12), healingMonths: 3)
            .FixedLab(Ago(12, 14, 30), "3500.00")
            .Finalize(Ago(12, 15, 15))
            .PayAll(Ago(12, 15, 25), "Visa");

        // ——— Fixed / Removable Prosthetics ———
        var crown = new VisitScript(plan, w, 35, "gp3", PatientStories.SalesFor(35));
        w.NeedPatient(35, Ago(9, 10));
        crown.Start(Ago(9, 12, 0), fromAppointment: false)
            .Notes(Ago(9, 12, 5), "تحضير ضرس علوي لتاج زيركون وأخذ الطبعة.")
            .Treat(Ago(9, 12, 10), "FixedProsthetics", "4200.00", ["24"], new { material = "Zirconia" })
            .FixedLab(Ago(9, 12, 15), "1200.00")
            .Finalize(Ago(9, 12, 45))
            .Pay(Ago(9, 12, 50), 2000m);

        var denture = new VisitScript(plan, w, 23, "gp6", PatientStories.SalesFor(23));
        w.NeedPatient(23, Ago(7, 10));
        denture.Start(Ago(7, 13, 0), fromAppointment: false)
            .Notes(Ago(7, 13, 5), "طقم كامل علوي. أخذ طبعة أولية واختيار لون الأسنان.")
            .Treat(Ago(7, 13, 10), "RemovableProsthetics", "7500.00", null, new { type = "Complete", material = "StandardAcrylic" })
            .FixedLab(Ago(7, 13, 15), "2000.00")
            .Finalize(Ago(7, 13, 45))
            .Pay(Ago(7, 13, 50), 1500m); // less than the lab invoice: the Doctor's entitlement stays pending

        // ——— Rate revisions: effective-dated, future collections only ———
        plan.At(Ago(20, 9, 0), "lab percentage revision", async () =>
            await w.Manager.PutAsync("/api/manager/settings/lab-percentage", new { percentage = "15.00", reason = "تحديث نسبة المعمل" }));
        plan.At(Ago(15, 9, 0), "sales commission revision", async () =>
            await w.Manager.PutAsync($"/api/manager/sales-employees/{w.SalesStaff("two").StaffId}/commission", new { commissionPercentage = "5.00", reason = "تحسين الأداء" }));
        plan.At(Ago(14, 9, 0), "doctor percentage revision", async () =>
            await w.Manager.PutAsync($"/api/manager/doctors/{w.Doctor("gp3").StaffId}/percentage", new { currentPercentage = "40.00" }));

        // ——— Patient Credit: overpayment, advance payment and applying credit ———
        plan.At(Ago(36, 17, 30), "overpayment becomes credit", async () =>
        {
            var sales = w.SalesSession(PatientStories.SalesFor(4));
            var balance = await sales.GetAsync($"/api/sales/patients/{w.Patients[4].Id}/balance");
            var charge = balance.GetProperty("charges").EnumerateArray().First(c => decimal.Parse(c.Str("outstanding"), System.Globalization.CultureInfo.InvariantCulture) > 0);
            await sales.PostKeyedAsync("/api/sales/payments", new
            {
                patientId = w.Patients[4].Id, amount = "1000.00", method = "Cash",
                allocations = new[] { new { chargeId = charge.Id(), amount = charge.Str("outstanding") } },
            });
            w.Bump("payments");
        });
        plan.At(Ago(2, 16, 0), "advance payment (no charge yet)", async () =>
        {
            var sales = w.SalesSession(PatientStories.SalesFor(44));
            await sales.PostKeyedAsync("/api/sales/payments", new { patientId = w.Patients[44].Id, amount = "500.00", method = "Cash", allocations = Array.Empty<object>() });
            w.Bump("payments");
            w.Bump("advance payments");
        });
        plan.At(Ago(8, 16, 0), "credit applied to a charge", async () =>
        {
            var sales = w.SalesSession(PatientStories.SalesFor(4));
            var balance = await sales.GetAsync($"/api/sales/patients/{w.Patients[4].Id}/balance");
            var credit = decimal.Parse(balance.Str("creditBalance"), System.Globalization.CultureInfo.InvariantCulture);
            var open = balance.GetProperty("charges").EnumerateArray().FirstOrDefault(c => decimal.Parse(c.Str("outstanding"), System.Globalization.CultureInfo.InvariantCulture) > 0);
            if (credit > 0 && open.ValueKind == JsonValueKind.Object)
            {
                var amount = Math.Min(credit, decimal.Parse(open.Str("outstanding"), System.Globalization.CultureInfo.InvariantCulture));
                await sales.PostKeyedAsync($"/api/sales/patients/{w.Patients[4].Id}/credit/apply", new { chargeId = open.Id(), amount = Json.Money(amount) });
                w.Bump("credit applications");
            }
        });

        // ——— Manager corrections: refund of credit, reversal of a mistaken payment, discount correction ———
        plan.At(Ago(1, 11, 0), "refund of advance credit", async () =>
        {
            var payment = await LastPaymentAsync(w, 44);
            await w.Manager.PostKeyedAsync($"/api/manager/payments/{payment}/refund", new { allocations = Array.Empty<object>(), creditAmount = "200.00", reason = "استرداد جزء من المبلغ المقدم بناءً على طلب المريض" });
            w.Bump("refunds");
        });
        plan.At(Ago(4, 11, 30), "reversal of a mistaken payment", async () =>
        {
            var payment = await LastPaymentAsync(w, 7);
            await w.Manager.PostKeyedAsync($"/api/manager/payments/{payment}/reversal", new { reason = "خطأ في إدخال المبلغ" });
            w.Bump("payment reversals");
        });
        plan.At(Ago(4, 12, 0), "discount correction on a charge", async () =>
        {
            var sales = w.SalesSession(PatientStories.SalesFor(8));
            var balance = await sales.GetAsync($"/api/sales/patients/{w.Patients[8].Id}/balance");
            var charge = balance.GetProperty("charges").EnumerateArray().First(c => decimal.Parse(c.Str("outstanding"), System.Globalization.CultureInfo.InvariantCulture) > 0);
            await w.Manager.PostAsync($"/api/manager/charges/{charge.Id()}/adjustments", new { kind = "DiscountCorrection", amountDelta = "-100.00", reason = "خصم استثنائي معتمد من الإدارة" });
            w.Bump("charge adjustments");
        });

        // ——— End of day: Doctor and Sales deactivation (the old work and entitlement stay with them) ———
        var endOfDay = new[] { w.Time.At(w.Time.Today, 20, 0), w.Time.RealNow.AddMinutes(45) }.Max();
        plan.At(endOfDay, "deactivate Doctor gp5", async () =>
        {
            await w.Manager.PostAsync($"/api/manager/staff/{w.Doctor("gp5").StaffId}/deactivate");
            w.Bump("inactive doctors");
        });
        plan.At(endOfDay.AddMinutes(10), "deactivate Sales three", async () =>
        {
            await w.Manager.PostAsync($"/api/manager/staff/{w.SalesStaff("three").StaffId}/deactivate");
            w.Bump("inactive sales employees");
        });
    }

    private static async Task<Guid> FirstCaseOfAsync(World w, int patient, string procedureCode)
    {
        var cases = await w.DoctorSession("surgeon").GetAsync($"/api/doctor/patients/{w.Patients[patient].Id}/treatment-cases");
        return cases.Items().First(c => c.Str("procedureCode") == procedureCode).Id();
    }

    private static async Task<Guid> LastPaymentAsync(World w, int patient)
    {
        var payments = await w.SalesSession(PatientStories.SalesFor(patient)).GetAsync($"/api/sales/patients/{w.Patients[patient].Id}/payments");
        var newest = payments.Items().First();
        return newest.TryGetProperty("paymentId", out var paymentId) ? paymentId.GetGuid() : newest.Id();
    }
}
