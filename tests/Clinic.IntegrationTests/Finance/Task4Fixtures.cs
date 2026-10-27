using System.Net;
using System.Text.Json;
using Clinic.IntegrationTests.Clinical;
using Clinic.IntegrationTests.Crm;
using Clinic.IntegrationTests.Infrastructure;

namespace Clinic.IntegrationTests.Finance;

/// <summary>One finalized priced line and the charge Finance established for it.</summary>
public sealed record FinalizedLine(Guid VisitId, Guid RecordId, Guid CaseId, Guid ChargeId);

public static class Task4
{
    public static decimal Money(this JsonElement e, string property) =>
        decimal.Parse(e.GetProperty(property).GetString()!, System.Globalization.CultureInfo.InvariantCulture);

    public static string M(decimal value) => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Appends a lab percentage revision effective now (FR-LAB-001); the history is never rewritten.</summary>
    public static async Task SetLabPercentageAsync(ApiClient manager, string percentage) =>
        await Http.JsonAsync(await manager.PutAsync("/api/manager/settings/lab-percentage", new { percentage, reason = "اختبار" }));

    /// <summary>
    /// Starts a Visit (from an arrived appointment), adds one treatment, prices it, optionally discounts it and enters the initial
    /// Fixed Lab, finalizes, and returns the established charge.
    /// </summary>
    public static async Task<FinalizedLine> FinalizedLineAsync(ApiFixture fixture, ApiClient doctor, ApiClient sales, Guid patientId, Guid doctorId,
        string procedureCode = "RootCanal", string price = "1500.00", string? discount = null, string? fixedLab = null, string[]? teeth = null)
    {
        var appointment = await Task3.BookAsync(sales, patientId, doctorId, Task3.Now(fixture));
        await Http.ExpectAsync(await sales.PostAsync($"/api/sales/appointments/{appointment.Id()}/arrival"), HttpStatusCode.Created);
        var visit = await Task3.StartVisitAsync(doctor, patientId, appointment.Id());
        var treatment = (await Task3.AddTreatmentAsync(doctor, visit.Id(), procedureCode, teeth ?? ["16"])).LastTreatment();
        var recordId = treatment.GetProperty("recordId").GetGuid();
        var caseId = treatment.GetProperty("case").Id();
        await Task3.PriceAsync(doctor, recordId, price);
        if (discount is not null)
        {
            var line = (await Task3.GetVisitAsync(doctor, visit.Id())).LastTreatment().GetProperty("ownPricing");
            await Http.JsonAsync(await doctor.PutAsync($"/api/doctor/pricing-lines/{line.GetProperty("lineId").GetString()}/discount",
                new { discount, expectedEntrySeq = 0 }));
        }

        if (fixedLab is not null)
        {
            await Http.JsonAsync(await doctor.PostAsync($"/api/doctor/fixed-lab/{caseId}", new { amount = fixedLab }), HttpStatusCode.Created);
        }

        await Task3.FinalizeAsync(doctor, visit.Id());
        var balance = await BalanceAsync(sales, patientId);
        var charge = balance.GetProperty("charges").EnumerateArray().Single(c => c.GetProperty("visitId").GetGuid() == visit.Id());
        return new FinalizedLine(visit.Id(), recordId, caseId, charge.Id());
    }

    public static async Task<JsonElement> BalanceAsync(ApiClient client, Guid patientId) =>
        await Http.JsonAsync(await client.GetAsync($"/api/sales/patients/{patientId}/balance"));

    public static JsonElement Charge(this JsonElement balance, Guid chargeId) =>
        balance.GetProperty("charges").EnumerateArray().Single(c => c.Id() == chargeId);

    public static async Task<JsonElement> PayAsync(ApiClient client, Guid patientId, string amount, params (Guid ChargeId, string Amount)[] allocations) =>
        await Http.JsonAsync(await client.PostKeyedAsync("/api/sales/payments", new
        {
            patientId, amount, method = "Cash", allocations = allocations.Select(a => new { chargeId = a.ChargeId, amount = a.Amount }).ToArray(),
        }), HttpStatusCode.Created);

    /// <summary>A Lead owned by <paramref name="sales"/> converted to a Lead-sourced patient (Original Sales = that employee).</summary>
    public static async Task<Guid> LeadPatientAsync(ApiClient sales)
    {
        var lead = await CrmFixtures.CreateLeadAsync(sales);
        var converted = await Http.JsonAsync(await sales.PostAsync($"/api/sales/leads/{lead.Id()}/convert", CrmFixtures.Conversion(), lead.Version()),
            HttpStatusCode.Created);
        return converted.GetProperty("patientId").GetGuid();
    }

    /// <summary>The previous clinic-local calendar month as "yyyy-MM" and its first day.</summary>
    public static (string Month, DateOnly Start) PreviousMonth(ApiFixture fixture)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        var local = TimeZoneInfo.ConvertTime(fixture.Factory.Clock.UtcNow, zone);
        var start = new DateOnly(local.Year, local.Month, 1).AddMonths(-1);
        return (start.ToString("yyyy-MM"), start);
    }

    /// <summary>Shifts the API clock into the middle of the previous clinic month (for payroll months that have ended).</summary>
    public static void IntoPreviousMonth(ApiFixture fixture)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        var local = TimeZoneInfo.ConvertTime(fixture.Factory.Clock.UtcNow, zone);
        var target = new DateTime(local.Year, local.Month, 1, 12, 0, 0).AddMonths(-1).AddDays(9);
        fixture.Factory.Clock.Advance(-(local.DateTime - target));
    }
}
