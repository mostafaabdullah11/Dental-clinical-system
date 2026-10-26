using System.Net;
using System.Text.Json;
using Clinic.IntegrationTests.Crm;
using Clinic.IntegrationTests.Infrastructure;
using Xunit;

namespace Clinic.IntegrationTests.Clinical;

/// <summary>F3.9 Doctor referral → Sales; F3.11 feedback request → public submission (no expiry) and the Sales-entered path.</summary>
public sealed class ReferralFeedbackTests(ApiFixture fixture)
{
    private const string ReferralNotes = "ملاحظة الطبيب السرية للتحويل";

    [Fact] // F3.9 / AC-REF-001..004: Sales sees destination, reason and instructions — never the Doctor's notes
    public async Task Doctor_referral_reaches_sales_without_notes()
    {
        using var w = await Task3.WorldAsync(fixture);
        var visit = await Task3.StartVisitAsync(w.DoctorClient, w.Patient.Id);

        await Http.ProblemAsync(await w.SalesClient.PostAsync($"/api/doctor/visits/{visit.Id()}/referrals",
            new { referralType = "Surgery", reason = "x", notes = (string?)null, followUpInstructions = (string?)null }), HttpStatusCode.Forbidden);

        var created = await Http.JsonAsync(await w.DoctorClient.PostAsync($"/api/doctor/visits/{visit.Id()}/referrals", new
        {
            referralType = "Surgery", reason = "خلع ضرس عقل مدفون", notes = ReferralNotes, followUpInstructions = "الحجز خلال أسبوع",
        }), HttpStatusCode.Created);
        Assert.Equal(ReferralNotes, created.GetProperty("notes").GetString());
        Assert.Equal("New", created.GetProperty("status").GetString());

        // New Doctor Referral is enqueued for every active Sales user in the same transaction.
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM notifications.notifications WHERE notification_type = 'NewDoctorReferral' AND related_entity_id = '{created.Id()}' AND recipient_user_id = '{w.Sales.UserId}'"));
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM notifications.notifications WHERE related_entity_id = '{created.Id()}' AND recipient_user_id = '{w.Doctor.UserId}'"));

        var list = await Task3.BodyAsync(await w.SalesClient.GetAsync("/api/sales/referrals?status=New"));
        Assert.Contains(created.Id().ToString(), list);
        Assert.DoesNotContain(ReferralNotes, list);
        var one = await Http.JsonAsync(await w.SalesClient.GetAsync($"/api/sales/referrals/{created.Id()}"));
        Assert.Equal("خلع ضرس عقل مدفون", one.GetProperty("reason").GetString());
        Assert.Equal("Surgery", one.GetProperty("referralType").GetString());
        Assert.Equal("الحجز خلال أسبوع", one.GetProperty("followUpInstructions").GetString());
        Assert.False(one.TryGetProperty("notes", out _));
        Assert.DoesNotContain(ReferralNotes, one.GetRawText());

        // Status work: If-Match required; a "notes" member is unknown (400); every change audited.
        await Http.ProblemAsync(await w.SalesClient.SendAsync(HttpMethod.Patch, $"/api/sales/referrals/{created.Id()}/status", new { status = "Contacted" }),
            HttpStatusCode.PreconditionRequired);
        await Http.ProblemAsync(await w.SalesClient.SendAsync(HttpMethod.Patch, $"/api/sales/referrals/{created.Id()}/status",
            new { status = "Contacted", notes = "x" }, one.Version()), HttpStatusCode.BadRequest);
        var contacted = await Http.JsonAsync(await w.SalesClient.SendAsync(HttpMethod.Patch, $"/api/sales/referrals/{created.Id()}/status",
            new { status = "Contacted" }, one.Version()));
        Assert.Equal("Contacted", contacted.GetProperty("status").GetString());
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM audit.audit_log WHERE action = 'crm.referral.status_changed' AND entity_id = '{created.Id()}'"));

        // The Sales clinical summary shows the referral reason and instructions, never the notes.
        var summary = await Task3.BodyAsync(await w.SalesClient.GetAsync($"/api/sales/patients/{w.Patient.Id}/clinical-summary"));
        Assert.Contains("خلع ضرس عقل مدفون", summary);
        Assert.DoesNotContain(ReferralNotes, summary);

        // The Doctor reads the notes; the Doctor cannot use the Sales list.
        Assert.Contains(ReferralNotes, await Task3.BodyAsync(await w.DoctorClient.GetAsync($"/api/doctor/patients/{w.Patient.Id}/referrals")));
        await Http.ProblemAsync(await w.DoctorClient.GetAsync("/api/sales/referrals"), HttpStatusCode.Forbidden);
    }

    [Fact] // F3.11 / AC-FDB-001..007, -010, -011: website flow end to end, no expiry, one result per request
    public async Task Feedback_request_to_public_submission()
    {
        using var w = await Task3.WorldAsync(fixture);
        var (visitId, _, _) = await Task3.PricedVisitAsync(fixture, w);
        await Task3.FinalizeAsync(w.DoctorClient, visitId);

        var queue = await Http.JsonAsync(await w.SalesClient.GetAsync("/api/sales/feedback-requests?status=Pending"));
        var request = Assert.Single(queue.EnumerateArray(), r => r.GetProperty("visitId").GetGuid() == visitId);
        Assert.False(request.TryGetProperty("rating", out _));
        var requestId = request.Id();
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM notifications.notifications WHERE notification_type = 'FeedbackRequestPending' AND related_entity_id = '{requestId}' AND recipient_user_id = '{w.Sales.UserId}'"));

        // Prepare the link twice: the earlier token stops working.
        var first = (await Http.JsonAsync(await w.SalesClient.PostAsync($"/api/sales/feedback-requests/{requestId}/link"))).GetProperty("url").GetString()!;
        var second = (await Http.JsonAsync(await w.SalesClient.PostAsync($"/api/sales/feedback-requests/{requestId}/link"))).GetProperty("url").GetString()!;
        var firstToken = first[(first.LastIndexOf('/') + 1)..];
        var token = second[(second.LastIndexOf('/') + 1)..];
        Assert.Equal(43, token.Length);
        Assert.DoesNotContain(visitId.ToString("N")[..8], token);
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM feedback.feedback_requests WHERE link_token_hash = '{token}'"));

        var sent = await Http.JsonAsync(await w.SalesClient.PostAsync($"/api/sales/feedback-requests/{requestId}/mark-sent", new { channel = "WhatsApp" }));
        Assert.Equal("Sent", sent.GetProperty("status").GetString());

        using var anonymous = fixture.Factory.CreateApiClient();
        var unknown = await anonymous.SendAsync(HttpMethod.Get, "/api/public/feedback/" + new string('A', 43), null, withCsrf: false);
        var malformed = await anonymous.SendAsync(HttpMethod.Get, "/api/public/feedback/bad-token", null, withCsrf: false);
        var stale = await anonymous.SendAsync(HttpMethod.Get, "/api/public/feedback/" + firstToken, null, withCsrf: false);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        Assert.Equal((await Http.ProblemAsync(unknown, HttpStatusCode.NotFound)).Code(), (await Http.ProblemAsync(malformed, HttpStatusCode.NotFound)).Code());

        // No expiry: a request and link 400 days old still open and accept the submission (the shared test clock is not
        // moved, so parallel tests are unaffected; there is no time input to the decision at all).
        await fixture.Database.ExecuteAsync(
            $"UPDATE feedback.feedback_requests SET created_at = created_at - interval '400 days', link_sent_at = link_sent_at - interval '400 days' WHERE id = '{requestId}'");
        {
            var form = await Http.JsonAsync(await anonymous.SendAsync(HttpMethod.Get, "/api/public/feedback/" + token, null, withCsrf: false));
            Assert.Equal("Open", form.GetProperty("state").GetString());
            Assert.StartsWith("د. ", form.GetProperty("doctorName").GetString());
            Assert.DoesNotContain(w.PatientName, form.GetRawText());
            Assert.DoesNotContain(w.Patient.Code, form.GetRawText());
            Assert.DoesNotContain(w.PatientPhone, form.GetRawText());

            await Http.ProblemAsync(await anonymous.SendAsync(HttpMethod.Post, "/api/public/feedback/" + token, new { rating = 6, comment = "x" }, withCsrf: false),
                HttpStatusCode.BadRequest);
            await Http.ProblemAsync(await anonymous.SendAsync(HttpMethod.Post, "/api/public/feedback/" + token,
                new { rating = 4, comment = "x", phone = "01012345678" }, withCsrf: false), HttpStatusCode.BadRequest);
            var ack = await Http.JsonAsync(await anonymous.SendAsync(HttpMethod.Post, "/api/public/feedback/" + token,
                new { rating = 4, comment = "<b>خدمة ممتازة</b>" }, withCsrf: false));
            Assert.Equal("Received", ack.GetProperty("state").GetString());
        }

        // A second submission and a Sales entry for the same Visit are refused; exactly one entry exists.
        Assert.Equal("feedback.already_submitted", (await Http.ProblemAsync(await anonymous.SendAsync(HttpMethod.Post, "/api/public/feedback/" + token,
            new { rating = 1, comment = "again" }, withCsrf: false), HttpStatusCode.Conflict)).Code());
        var neutral = await Http.JsonAsync(await anonymous.SendAsync(HttpMethod.Get, "/api/public/feedback/" + token, null, withCsrf: false));
        Assert.Equal("feedback.already_submitted", neutral.GetProperty("state").GetString());
        Assert.DoesNotContain("خدمة ممتازة", neutral.GetRawText());
        Assert.Equal("feedback.already_submitted", (await Http.ProblemAsync(await w.SalesClient.PostAsync("/api/sales/feedback-entries",
            new { patientId = w.Patient.Id, doctorId = w.Doctor.StaffId, visitId, rating = 5, comment = "x" }), HttpStatusCode.Conflict)).Code());
        Assert.Equal("feedback.already_submitted", (await Http.ProblemAsync(
            await w.SalesClient.PostAsync($"/api/sales/feedback-requests/{requestId}/link"), HttpStatusCode.Conflict)).Code());
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM feedback.feedback_entries WHERE feedback_request_id = '{requestId}'"));
        Assert.Equal("<b>خدمة ممتازة</b>", await fixture.Database.ScalarAsync<string>(
            $"SELECT comment FROM feedback.feedback_entries WHERE feedback_request_id = '{requestId}'")); // stored as plain text

        var submittedQueue = await Http.JsonAsync(await w.SalesClient.GetAsync("/api/sales/feedback-requests?status=Submitted"));
        Assert.Contains(submittedQueue.EnumerateArray(), r => r.Id() == requestId);
        Assert.DoesNotContain("خدمة ممتازة", submittedQueue.GetRawText());

        // Tokens never reach logs or audit.
        Assert.DoesNotContain(token, string.Join('\n', fixture.Factory.Logs.Lines));
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM audit.audit_log WHERE coalesce(previous_value::text, '') LIKE '%{token}%' OR coalesce(new_value::text, '') LIKE '%{token}%'"));

        // Results: Manager only, with the source shown.
        var results = await Http.JsonAsync(await w.Manager.GetAsync($"/api/manager/feedback?doctorId={w.Doctor.StaffId}&source=PatientWeb"));
        var entry = Assert.Single(results.EnumerateArray());
        Assert.Equal(4, entry.GetProperty("rating").GetInt32());
        Assert.Equal("PatientWeb", entry.GetProperty("source").GetString());
        await Http.ProblemAsync(await w.SalesClient.GetAsync("/api/manager/feedback"), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await w.DoctorClient.GetAsync("/api/manager/feedback"), HttpStatusCode.Forbidden);
    }

    [Fact] // AC-FDB-008..009: Sales-entered feedback links the Visit's request (then the token answers already-submitted); general feedback
    public async Task Sales_entered_feedback_links_the_request_and_general_feedback_has_none()
    {
        using var w = await Task3.WorldAsync(fixture);
        var (visitId, _, _) = await Task3.PricedVisitAsync(fixture, w);
        await Task3.FinalizeAsync(w.DoctorClient, visitId);
        var requestId = Guid.Parse((await fixture.Database.ScalarAsync<string>($"SELECT id::text FROM feedback.feedback_requests WHERE visit_id = '{visitId}'"))!);
        var url = (await Http.JsonAsync(await w.SalesClient.PostAsync($"/api/sales/feedback-requests/{requestId}/link"))).GetProperty("url").GetString()!;
        var token = url[(url.LastIndexOf('/') + 1)..];

        // Visit mismatch and rating outside 1..5 are rejected.
        var otherDoctor = await fixture.CreateDoctorAsync(w.Manager);
        await Http.ProblemAsync(await w.SalesClient.PostAsync("/api/sales/feedback-entries",
            new { patientId = w.Patient.Id, doctorId = otherDoctor.StaffId, visitId, rating = 5, comment = "x" }), HttpStatusCode.UnprocessableEntity);
        await Http.ProblemAsync(await w.SalesClient.PostAsync("/api/sales/feedback-entries",
            new { patientId = w.Patient.Id, doctorId = w.Doctor.StaffId, visitId, rating = 0, comment = "x" }), HttpStatusCode.BadRequest);

        var created = await Http.JsonAsync(await w.SalesClient.PostAsync("/api/sales/feedback-entries",
            new { patientId = w.Patient.Id, doctorId = w.Doctor.StaffId, visitId, rating = 5, comment = "الطبيب ممتاز" }), HttpStatusCode.Created);
        Assert.Equal(["id", "recordedAt"], created.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("SalesEntered", await fixture.Database.ScalarAsync<string>(
            $"SELECT source FROM feedback.feedback_entries WHERE feedback_request_id = '{requestId}'"));

        using var anonymous = fixture.Factory.CreateApiClient();
        Assert.Equal("feedback.already_submitted", (await Http.JsonAsync(await anonymous.SendAsync(HttpMethod.Get, "/api/public/feedback/" + token, null,
            withCsrf: false))).GetProperty("state").GetString());
        Assert.Equal("feedback.already_submitted", (await Http.ProblemAsync(await anonymous.SendAsync(HttpMethod.Post, "/api/public/feedback/" + token,
            new { rating = 3, comment = (string?)null }, withCsrf: false), HttpStatusCode.Conflict)).Code());

        // General feedback: no Visit, no request; audited; no notification; nothing in a clinical table.
        var general = await Http.JsonAsync(await w.SalesClient.PostAsync("/api/sales/feedback-entries",
            new { patientId = w.Patient.Id, doctorId = w.Doctor.StaffId, visitId = (Guid?)null, rating = 3, comment = "عام" }), HttpStatusCode.Created);
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM feedback.feedback_entries WHERE id = '{general.Id()}' AND visit_id IS NULL AND feedback_request_id IS NULL"));
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM audit.audit_log WHERE entity_id = '{general.Id()}'"));
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM notifications.notifications WHERE related_entity_id = '{general.Id()}'"));
        await Http.ProblemAsync(await w.DoctorClient.PostAsync("/api/sales/feedback-entries",
            new { patientId = w.Patient.Id, doctorId = w.Doctor.StaffId, visitId = (Guid?)null, rating = 3, comment = "x" }), HttpStatusCode.Forbidden);

        // The database enforces the shape: a Visit without its request (or a second entry for a request) is rejected.
        Assert.Equal("23514", await fixture.Database.TryExecuteAsync($"""
            INSERT INTO feedback.feedback_entries (id, source, patient_id, doctor_id, visit_id, feedback_request_id, rating, recorded_by_user_id, recorded_at)
            VALUES (gen_random_uuid(), 'SalesEntered', '{w.Patient.Id}', '{w.Doctor.StaffId}', '{visitId}', NULL, 3, '{w.Sales.UserId}', now())
            """));
        Assert.Equal("23505", await fixture.Database.TryExecuteAsync($"""
            INSERT INTO feedback.feedback_entries (id, source, patient_id, doctor_id, visit_id, feedback_request_id, rating, recorded_by_user_id, recorded_at)
            VALUES (gen_random_uuid(), 'SalesEntered', '{w.Patient.Id}', '{w.Doctor.StaffId}', '{visitId}', '{requestId}', 3, '{w.Sales.UserId}', now())
            """));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync(
            $"UPDATE feedback.feedback_entries SET rating = 1 WHERE id = '{general.Id()}'", "clinic_app"));
    }

    [Fact] // AC-FDB-010: a website submission and a Sales entry racing for one request produce exactly one entry
    public async Task Concurrent_submissions_produce_one_entry()
    {
        using var w = await Task3.WorldAsync(fixture);
        var (visitId, _, _) = await Task3.PricedVisitAsync(fixture, w);
        await Task3.FinalizeAsync(w.DoctorClient, visitId);
        var requestId = Guid.Parse((await fixture.Database.ScalarAsync<string>($"SELECT id::text FROM feedback.feedback_requests WHERE visit_id = '{visitId}'"))!);
        var url = (await Http.JsonAsync(await w.SalesClient.PostAsync($"/api/sales/feedback-requests/{requestId}/link"))).GetProperty("url").GetString()!;
        var token = url[(url.LastIndexOf('/') + 1)..];

        using var anonymous = fixture.Factory.CreateApiClient();
        var responses = await Task.WhenAll(
            anonymous.SendAsync(HttpMethod.Post, "/api/public/feedback/" + token, new { rating = 5, comment = (string?)null }, withCsrf: false),
            w.SalesClient.PostAsync("/api/sales/feedback-entries", new { patientId = w.Patient.Id, doctorId = w.Doctor.StaffId, visitId, rating = 2, comment = "x" }));
        Assert.Single(responses, r => r.IsSuccessStatusCode);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM feedback.feedback_entries WHERE feedback_request_id = '{requestId}'"));
    }
}
