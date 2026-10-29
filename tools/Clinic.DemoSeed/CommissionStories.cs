using System.Globalization;
using System.Text.Json;

namespace Clinic.DemoSeed;

/// <summary>
/// Sales commission is calculated on Collected Money with the rate effective at the time of collection, and a commission rate is
/// effective from the DATABASE clock at the moment it was saved: never back-dated (Q-COMM-1, frozen). The replayed history therefore
/// has no commission-bearing collections before the demo run itself. These steps collect on Lead-sourced patients' open balances
/// just after the rates were saved, and draft this month's Sales payroll, so commission, the performance report and the payroll
/// sheet show real figures produced by the real rules.
/// </summary>
internal static class CommissionStories
{
    public static void Register(Plan plan, World w)
    {
        // The rates are saved within seconds of the run start; collections a few minutes later are on the right side of that instant.
        var first = w.Time.RealNow.AddMinutes(12);
        var k = 0;
        foreach (var patient in PatientStories.LeadSourced.Keys)
        {
            var index = patient;
            var salesKey = PatientStories.SalesFor(index);
            plan.At(first.AddMinutes(k++), $"collect on lead-sourced patient {index}", async () =>
            {
                var sales = w.SalesSession(salesKey);
                var balance = await sales.GetAsync($"/api/sales/patients/{w.Patients[index].Id}/balance");
                var open = balance.GetProperty("charges").EnumerateArray()
                    .Select(c => (Id: c.Id(), Outstanding: decimal.Parse(c.Str("outstanding"), CultureInfo.InvariantCulture)))
                    .FirstOrDefault(c => c.Outstanding > 0);
                if (open.Outstanding <= 0)
                {
                    return;
                }

                var amount = Math.Min(open.Outstanding, 600m);
                await sales.PostKeyedAsync("/api/sales/payments", new
                {
                    patientId = w.Patients[index].Id, amount = Json.Money(amount), method = "Cash",
                    allocations = new[] { new { chargeId = open.Id, amount = Json.Money(amount) } },
                });
                w.Bump("payments");
                w.Bump("collections after the commission rates took effect");
            });
        }

        // A Draft for the current month: its commission items come from the collections above (a Draft can be created before month end).
        plan.At(first.AddMinutes(k + 5), "current-month Sales payroll drafts", async () =>
        {
            var month = w.Time.Today.ToString("yyyy-MM");
            foreach (var key in new[] { "demo", "two" })
            {
                await w.Manager.PostAsync("/api/manager/payroll/sales/drafts", new { salesEmployeeId = w.SalesStaff(key).StaffId, month });
                w.Bump("payroll sheets (sales, current month draft)");
            }
        });
    }
}
