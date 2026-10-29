using System.Text.Json;

namespace Clinic.DemoSeed;

/// <summary>
/// Feedback requests are created by Visit finalization (Pending). Over the following days Sales marks some as Sent, patients
/// answer some through the secure link (no expiry), and Sales records a few answers a patient gave in person (Sales-entered, linked
/// to the existing request so it is never counted twice).
/// </summary>
internal static class FeedbackStories
{
    private static readonly string[] Comments =
    [
        "الطبيب ممتاز وشرح الخطة بوضوح.", "الانتظار كان طويلاً قليلاً لكن العلاج جيد.", "تجربة مريحة وفريق متعاون.",
        "أنصح بالعيادة، العلاج بدون ألم.", "تحسن كبير بعد الجلسة.", "الأسعار مناسبة والمعاملة راقية.", "الموعد تأخر عشر دقائق.",
    ];

    public static void Register(Plan plan, World w, int maxAgo)
    {
        DateTimeOffset Ago(int n, int h, int m = 0) => w.Time.Ago(Journeys.Scale(n, maxAgo), h, m);

        // Round 1 — Sales sends links for a share of the older requests.
        plan.At(Ago(14, 18, 0), "feedback: send links (round 1)", () => SendAsync(w, "Pending", every: 4, offset: 0));
        // Round 2 — some patients answer through the link; more links are sent.
        plan.At(Ago(8, 18, 0), "feedback: answers via link (round 2)", async () =>
        {
            await AnswerAsync(w, "Sent", every: 2, offset: 0);
            await SendAsync(w, "Pending", every: 5, offset: 1);
        });
        // Round 3 — Sales records feedback told in person; more answers come in.
        plan.At(Ago(3, 18, 0), "feedback: in-person entries (round 3)", async () =>
        {
            await RecordInPersonAsync(w, every: 6, offset: 1);
            await AnswerAsync(w, "Sent", every: 3, offset: 1);
        });
        // Today — the last links go out.
        plan.At(w.Time.At(w.Time.Today, 19, 0), "feedback: send links (today)", () => SendAsync(w, "Pending", every: 7, offset: 2));
    }

    private static async Task<List<JsonElement>> QueueAsync(World w, string status) =>
        (await w.SalesSession("demo").GetAsync($"/api/sales/feedback-requests?status={status}")).Items().ToList();

    private static async Task SendAsync(World w, string status, int every, int offset)
    {
        var queue = await QueueAsync(w, status);
        for (var k = 0; k < queue.Count; k++)
        {
            if (k % every == offset)
            {
                await w.SalesSession("demo").PostAsync($"/api/sales/feedback-requests/{queue[k].Id()}/link");
                await w.SalesSession("demo").PostAsync($"/api/sales/feedback-requests/{queue[k].Id()}/mark-sent", new { channel = "WhatsApp" });
                w.Bump("feedback sent");
            }
        }
    }

    private static async Task AnswerAsync(World w, string status, int every, int offset)
    {
        var queue = await QueueAsync(w, status);
        for (var k = 0; k < queue.Count; k++)
        {
            if (k % every != offset)
            {
                continue;
            }

            var link = await w.SalesSession("demo").PostAsync($"/api/sales/feedback-requests/{queue[k].Id()}/link");
            var token = link.Str("url").TrimEnd('/').Split('/').Last();
            await w.Public.PostAsync($"/api/public/feedback/{token}", new { rating = Rating(k), comment = k % 3 == 0 ? (string?)null : Comments[k % Comments.Length] });
            w.Bump("feedback submitted (patient link)");
        }
    }

    private static async Task RecordInPersonAsync(World w, int every, int offset)
    {
        var queue = await QueueAsync(w, "Pending");
        for (var k = 0; k < queue.Count; k++)
        {
            if (k % every == offset)
            {
                var item = queue[k];
                await w.SalesSession("demo").PostAsync("/api/sales/feedback-entries", new
                {
                    patientId = item.GetProperty("patient").Id(), doctorId = item.GetProperty("doctor").Id(), visitId = item.GuidOf("visitId"),
                    rating = Rating(k), comment = Comments[(k + 2) % Comments.Length],
                });
                w.Bump("feedback submitted (Sales-entered)");
            }
        }
    }

    private static int Rating(int k) => k % 9 == 4 ? 2 : k % 11 == 6 ? 3 : k % 4 == 0 ? 4 : 5;
}
