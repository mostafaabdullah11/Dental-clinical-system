using System.Net;
using System.Text.Json;
using Clinic.Api.Composition;
using Clinic.IntegrationTests.Crm;
using Clinic.IntegrationTests.Infrastructure;
using Clinic.Modules.Feedback.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Clinic.IntegrationTests.Clinical;

/// <summary>AC-SCH-013, AC-FIN-023, AC-FIN-025, AC-CLN-009, AC-CLN-011, AC-FDB-005 rate limits, AC-NEG-017 / -027 / -041.</summary>
public sealed class Task3AdditionalTests(ApiFixture fixture)
{
    [Fact] // AC-SCH-013: finalizing early releases the unused booked time; a late finalize never extends the booking
    public async Task Finalize_releases_unused_time_without_extending()
    {
        using var w = await Task3.WorldAsync(fixture);
        var now = Task3.Now(fixture);

        var early = await Task3.BookAsync(w.SalesClient, w.Patient.Id, w.Doctor.StaffId, now, duration: 240);
        var earlyVisit = await Task3.StartVisitAsync(w.DoctorClient, w.Patient.Id, early.Id());
        await Task3.FinalizeAsync(w.DoctorClient, earlyVisit.Id());
        var earlyAfter = await Http.JsonAsync(await w.SalesClient.GetAsync($"/api/sales/appointments/{early.Id()}"));
        var visitEnded = earlyAfter.GetProperty("visitEndedAt").GetDateTimeOffset();
        Assert.Equal("Completed", earlyAfter.GetProperty("status").GetString());
        Assert.Equal(240, earlyAfter.GetProperty("durationMinutes").GetInt32()); // the row is otherwise unchanged
        Assert.True(earlyAfter.GetProperty("effectiveEndAt").GetDateTimeOffset() < now.AddMinutes(240));
        Assert.Equal(visitEnded, earlyAfter.GetProperty("effectiveEndAt").GetDateTimeOffset());

        var slots = await Http.JsonAsync(await w.SalesClient.GetAsync(
            $"/api/sales/availability/slots?doctorId={w.Doctor.StaffId}&from={DateOnly.FromDateTime(now.UtcDateTime.AddDays(-1)):yyyy-MM-dd}&to={DateOnly.FromDateTime(now.UtcDateTime.AddDays(1)):yyyy-MM-dd}"));
        var booked = Assert.Single(slots.GetProperty("booked").EnumerateArray(), b => b.GetProperty("appointmentId").GetGuid() == early.Id());
        Assert.Equal(visitEnded, booked.GetProperty("effectiveEndAt").GetDateTimeOffset());

        // Nominal end already passed: the effective end stays at start + duration.
        var late = await Task3.BookAsync(w.SalesClient, w.Patient.Id, w.Doctor.StaffId, now.AddHours(-3), duration: 30);
        var lateVisit = await Task3.StartVisitAsync(w.DoctorClient, w.Patient.Id, late.Id());
        await Task3.FinalizeAsync(w.DoctorClient, lateVisit.Id());
        var lateAfter = await Http.JsonAsync(await w.SalesClient.GetAsync($"/api/sales/appointments/{late.Id()}"));
        Assert.Equal(now.AddHours(-3).AddMinutes(30), lateAfter.GetProperty("effectiveEndAt").GetDateTimeOffset());
    }

    [Fact] // AC-FIN-025 / AC-FIN-023 / AC-CLN-009: price never below the discount; concurrent discounts serialize; snapshot frozen
    public async Task Pricing_invariants_and_discount_concurrency()
    {
        using var w = await Task3.WorldAsync(fixture);
        var visit = await Task3.StartVisitAsync(w.DoctorClient, w.Patient.Id);
        var record = (await Task3.AddTreatmentAsync(w.DoctorClient, visit.Id(), "FixedProsthetics", ["24"], new { material = "Zirconia" })).LastTreatment();
        var priced = await Task3.PriceAsync(w.DoctorClient, record.GetProperty("recordId").GetGuid(), "3000.00");
        var own = priced.LastTreatment().GetProperty("ownPricing");
        var lineId = own.GetProperty("lineId").GetString()!;
        Assert.Equal("40.00", await fixture.Database.ScalarAsync<string>(
            $"SELECT doctor_percentage_snapshot::text FROM clinical.case_work_items WHERE id = '{own.GetProperty("workItemId").GetGuid()}'"));

        // Doctor and Sales race with the same expected entry: exactly one wins, the other gets discount.stale_entry.
        var race = await Task.WhenAll(
            w.DoctorClient.PutAsync($"/api/doctor/pricing-lines/{lineId}/discount", new { discount = "1000.00", expectedEntrySeq = 0 }),
            w.SalesClient.PutAsync($"/api/sales/pricing-lines/{lineId}/discount", new { discount = "900.00", expectedEntrySeq = 0 }));
        Assert.Single(race, r => r.IsSuccessStatusCode);
        var loser = Assert.Single(race, r => !r.IsSuccessStatusCode);
        Assert.Equal("discount.stale_entry", (await Http.ProblemAsync(loser, HttpStatusCode.Conflict)).Code());
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM clinical.pricing_discount_entries WHERE case_work_item_id = '{own.GetProperty("workItemId").GetGuid()}'"));

        // Lowering the price below the current discount is refused; the price is unchanged.
        var current = (await Task3.GetVisitAsync(w.DoctorClient, visit.Id())).LastTreatment().GetProperty("ownPricing");
        var lower = await w.DoctorClient.PutAsync($"/api/doctor/treatment-records/{record.GetProperty("recordId").GetGuid()}/pricing",
            new { price = "800.00" }, current.Version());
        Assert.Equal("pricing.below_discount", (await Http.ProblemAsync(lower, HttpStatusCode.UnprocessableEntity)).Code());
        Assert.Equal("3000.00", (await Task3.GetVisitAsync(w.DoctorClient, visit.Id())).LastTreatment().GetProperty("ownPricing").GetProperty("price").GetString());

        // G6 backstop: a discount entry above the price is rejected by the database itself.
        Assert.Equal("23514", await fixture.Database.TryExecuteAsync($"""
            INSERT INTO clinical.pricing_discount_entries (id, pricing_line_kind, case_work_item_id, entry_seq, amount, set_by_user_id, set_by_role, set_at)
            VALUES (gen_random_uuid(), 'WorkItem', '{own.GetProperty("workItemId").GetGuid()}', 99, 5000, '{w.Sales.UserId}', 'SALES_RECEPTION', now())
            """));
    }

    [Fact] // AC-CLN-002 / -008 / -013: a second Doctor continues the case with own priced work; every Doctor reads, only the owner edits
    public async Task Second_doctor_continues_a_case_with_own_work()
    {
        using var w = await Task3.WorldAsync(fixture);
        var (visitA, _, recordA) = await Task3.PricedVisitAsync(fixture, w);
        var caseId = (await Task3.GetVisitAsync(w.DoctorClient, visitA)).LastTreatment().GetProperty("case").Id();
        await Task3.FinalizeAsync(w.DoctorClient, visitA);

        var doctorB = await fixture.CreateDoctorAsync(w.Manager, "35.00");
        using var b = await fixture.LoginAsync(doctorB.UserName);
        var visitB = await Task3.StartVisitAsync(b, w.Patient.Id);
        var continued = (await Http.JsonAsync(await b.PostAsync($"/api/doctor/visits/{visitB.Id()}/treatments", new { caseId }), HttpStatusCode.Created)).LastTreatment();
        Assert.False(continued.GetProperty("startedInThisVisit").GetBoolean());
        Assert.Equal(caseId, continued.GetProperty("case").Id());

        // B cannot change the case details that belong to A's (finalized) starting Visit.
        var locked = await b.PutAsync($"/api/doctor/visits/{visitB.Id()}/treatments/{continued.GetProperty("recordId").GetGuid()}",
            new { teeth = new[] { "17" }, area = (string?)null, details = (object?)null });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, locked.StatusCode);

        // B prices own work on the same case: two work items, two performing Doctors, each with own percentage snapshot.
        await Task3.PriceAsync(b, continued.GetProperty("recordId").GetGuid(), "500.00");
        Assert.Equal(new[] { "35.00", "40.00" }, (await fixture.Database.ColumnAsync(
            $"SELECT doctor_percentage_snapshot::text FROM clinical.case_work_items WHERE treatment_case_id = '{caseId}' ORDER BY 1")).ToArray());

        // Read breadth: B reads A's Visit (not own, not editable) and the whole case; B cannot touch A's work item.
        var aVisitByB = await Task3.GetVisitAsync(b, visitA);
        Assert.False(aVisitByB.GetProperty("isOwn").GetBoolean());
        Assert.False(aVisitByB.GetProperty("canEdit").GetBoolean());
        Assert.Equal(JsonValueKind.Null, aVisitByB.LastTreatment().GetProperty("ownPricing").ValueKind); // another Doctor's price is not shown
        Assert.Equal(2, (await Http.JsonAsync(await b.GetAsync($"/api/doctor/treatment-cases/{caseId}"))).GetProperty("visits").GetArrayLength());
        await Http.ProblemAsync(await b.PutAsync($"/api/doctor/treatment-records/{recordA}/pricing", new { price = "1.00" }), HttpStatusCode.NotFound);
    }

    [Fact] // AC-CLN-011: case lifecycle with If-Match; a completed case stays readable; status history is audited
    public async Task Treatment_case_lifecycle()
    {
        using var w = await Task3.WorldAsync(fixture);
        var visit = await Task3.StartVisitAsync(w.DoctorClient, w.Patient.Id);
        var record = (await Task3.AddTreatmentAsync(w.DoctorClient, visit.Id(), "Extraction", ["38"], new { extraction_type = "Surgical" })).LastTreatment();
        var caseRow = record.GetProperty("case");
        Assert.Equal("Active", caseRow.GetProperty("status").GetString());

        await Http.ProblemAsync(await w.DoctorClient.PutAsync($"/api/doctor/visits/{visit.Id()}/cases/{caseRow.Id()}/status", new { status = "Completed" }),
            HttpStatusCode.PreconditionRequired);
        var completed = await Http.JsonAsync(await w.DoctorClient.PutAsync($"/api/doctor/visits/{visit.Id()}/cases/{caseRow.Id()}/status",
            new { status = "Completed" }, caseRow.Version()));
        Assert.Equal("Completed", completed.LastTreatment().GetProperty("case").GetProperty("status").GetString());
        await Http.ProblemAsync(await w.DoctorClient.PutAsync($"/api/doctor/visits/{visit.Id()}/cases/{caseRow.Id()}/status",
            new { status = "Arrived" }, completed.LastTreatment().GetProperty("case").Version()), HttpStatusCode.BadRequest);

        var history = await Http.JsonAsync(await w.DoctorClient.GetAsync($"/api/doctor/treatment-cases/{caseRow.Id()}"));
        Assert.Equal("Completed", history.GetProperty("case").GetProperty("status").GetString());
        Assert.Single(history.GetProperty("visits").EnumerateArray());
    }

    [Fact] // AC-FDB-005 / AC-NEG-041: per-token and per-IP limits answer 429; the public routes need only the token
    public async Task Public_feedback_is_rate_limited_per_token_and_per_ip()
    {
        using var w = await Task3.WorldAsync(fixture);
        var (visitId, _, _) = await Task3.PricedVisitAsync(fixture, w);
        await Task3.FinalizeAsync(w.DoctorClient, visitId);
        var requestId = Guid.Parse((await fixture.Database.ScalarAsync<string>($"SELECT id::text FROM feedback.feedback_requests WHERE visit_id = '{visitId}'"))!);
        var url = (await Http.JsonAsync(await w.SalesClient.PostAsync($"/api/sales/feedback-requests/{requestId}/link"))).GetProperty("url").GetString()!;
        var token = url[(url.LastIndexOf('/') + 1)..];

        await using (var perToken = new ClinicApiFactory(fixture.Database, configureServices: s => s.Configure<FeedbackOptions>(o => o.PerTokenPerMinute = 3)))
        {
            using var anonymous = perToken.CreateApiClient();
            for (var i = 0; i < 3; i++)
            {
                Assert.Equal(HttpStatusCode.OK, (await anonymous.SendAsync(HttpMethod.Get, "/api/public/feedback/" + token, null, withCsrf: false)).StatusCode);
            }

            Assert.Equal("rate_limited", (await Http.ProblemAsync(await anonymous.SendAsync(HttpMethod.Get, "/api/public/feedback/" + token, null, withCsrf: false),
                HttpStatusCode.TooManyRequests)).Code());
        }

        await using (var perIp = new ClinicApiFactory(fixture.Database, publicFeedbackPerIpRateLimit: 2))
        {
            using var anonymous = perIp.CreateApiClient();
            for (var i = 0; i < 2; i++)
            {
                await anonymous.SendAsync(HttpMethod.Get, "/api/public/feedback/" + new string((char)('A' + i), 43), null, withCsrf: false);
            }

            Assert.Equal(HttpStatusCode.TooManyRequests,
                (await anonymous.SendAsync(HttpMethod.Get, "/api/public/feedback/" + new string('Z', 43), null, withCsrf: false)).StatusCode);
        }

        // No patient login / account / portal route exists anywhere.
        var routes = EndpointAuthorizationValidator.Describe(fixture.Factory.Services.GetRequiredService<EndpointDataSource>());
        Assert.DoesNotContain(routes, r => r.Route.Contains("/patient-portal", StringComparison.OrdinalIgnoreCase)
                                           || r.Route.StartsWith("/api/patient/", StringComparison.OrdinalIgnoreCase)
                                           || r.Route.Contains("reopen", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // AC-NEG-017 / -020 / -023 / -027: no separatelyPricedWork, no percentage discount, no Manager initial price, no reopen
    public async Task Negative_contract_hygiene()
    {
        using var manager = await fixture.ManagerAsync();
        var all = string.Join('\n', await Task.WhenAll(
            new[] { "doctor", "sales", "manager" }.Select(async d => await (await manager.GetAsync($"/openapi/{d}.json")).Content.ReadAsStringAsync())));
        Assert.DoesNotContain("separatelyPricedWork", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("discountPercent", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/reopen", all, StringComparison.OrdinalIgnoreCase);

        var managerDoc = JsonDocument.Parse(await (await manager.GetAsync("/openapi/manager.json")).Content.ReadAsStringAsync());
        var managerPaths = managerDoc.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.DoesNotContain(managerPaths, p => p.Contains("pricing", StringComparison.Ordinal) || p.Contains("visits", StringComparison.Ordinal));
        // Task 4: the Manager only revises an existing Fixed Lab (PUT, never the initial POST) and edits established ortho terms /
        // schedule; the initial Fixed Lab and initial ortho terms stay Doctor-only.
        var paths = managerDoc.RootElement.GetProperty("paths");
        Assert.Equal(["/api/manager/fixed-lab/{caseId}"], managerPaths.Where(p => p.Contains("fixed-lab", StringComparison.Ordinal)).ToList());
        Assert.False(paths.GetProperty("/api/manager/fixed-lab/{caseId}").TryGetProperty("post", out _));
        Assert.Equal(["/api/manager/orthodontic-cases/{id}/installments", "/api/manager/orthodontic-cases/{id}/terms"],
            managerPaths.Where(p => p.StartsWith("/api/manager/orthodontic-cases", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList());
    }
}
