namespace Clinic.DemoSeed;

/// <summary>
/// Today's clinic board and the days around it: every appointment status (Scheduled, Confirmed, InTreatment, Completed,
/// Cancelled, NoShow, Rescheduled), patients who arrived and wait, an overlapping booking, a started and an unfinished Visit.
/// </summary>
internal static class BoardStories
{
    public static void Register(Plan plan, World w)
    {
        DateTimeOffset Today(int h, int m = 0) => w.Time.At(w.Time.Today, h, m);
        DateTimeOffset Tomorrow(int h, int m = 0) => w.Time.Ahead(1, h, m);

        // Patients who first appear on the board are registered a few days earlier.
        for (var p = 33; p < Cast.PatientCount; p++)
        {
            w.NeedPatient(p, w.Time.Ago(3, 10, p - 20));
        }

        VisitScript Booked(int p, string doc, DateTimeOffset start, int minutes = 30, string reason = "كشف وعلاج", bool confirmed = false)
        {
            var v = new VisitScript(plan, w, p, doc, PatientStories.SalesFor(p));
            // Booked the previous afternoon (or this morning for a day that is still ahead).
            var bookAt = start > Today(23, 59) ? Today(8, 20 + p % 35) : start.AddHours(-20);
            v.Book(bookAt, start, minutes, reason);
            if (confirmed)
            {
                v.Confirm(bookAt.AddMinutes(30));
            }

            return v;
        }

        static string[] Teeth(params string[] teeth) => teeth;

        // ——— Dr. Karim (doctor.demo): the fullest day ———
        var done = Booked(33, "demo", Today(9, 30), reason: "حشو", confirmed: true);
        done.Arrive(Today(9, 20)).Start(Today(9, 32)).Notes(Today(9, 34), "حشو جيري ضرس علوي. تم تحضير التجويف وعزل الحقل.")
            .Treat(Today(9, 38), "Filling", "450.00", Teeth("15"), new { filling_type = "GIC" }).Finalize(Today(10, 5)).PayAll(Today(10, 10));

        var treating = Booked(34, "demo", Today(10, 30), reason: "علاج عصب", confirmed: true);
        treating.Arrive(Today(10, 20)).Start(Today(10, 35)).Notes(Today(10, 37), "بدء علاج عصب ضرس سفلي. المريضة تشكو من ألم عند البرودة.")
            .Treat(Today(10, 42), "RootCanal", "2000.00", Teeth("36"));

        Booked(35, "demo", Today(11, 30), confirmed: true).Arrive(Today(11, 25));
        Booked(36, "demo", Today(12, 0)).Arrive(Today(11, 50));
        Booked(37, "demo", Today(14, 0), confirmed: true);
        Booked(38, "demo", Today(16, 0));
        Booked(39, "demo", Today(16, 15)); // overlaps the booking above: same Doctor, allowed
        Booked(40, "demo", Today(17, 30)).SetStatus(Today(8, 30), "Cancelled", "اعتذرت المريضة لظرف عائلي");
        Booked(41, "demo", Today(9, 0)).SetStatus(Today(9, 45), "NoShow");
        Booked(42, "demo", Today(13, 0)).Reschedule(Today(8, 45), Tomorrow(13, 0), 30, "طلب المريض تأجيل الموعد");
        Booked(43, "demo", Tomorrow(10, 0));
        Booked(44, "demo", Tomorrow(11, 0), confirmed: true);

        // ——— Oral surgeon ———
        var surgery = Booked(45, "surgeon", Today(12, 30), 45, "خلع جراحي", confirmed: true);
        surgery.Arrive(Today(12, 20)).Start(Today(12, 35)).Notes(Today(12, 37), "خلع جراحي لضرس العقل السفلي مع خياطة. تعليمات ما بعد الجراحة.")
            .Treat(Today(12, 42), "Extraction", "1100.00", Teeth("38"), new { extraction_type = "Surgical" }).Finalize(Today(13, 25)).Pay(Today(13, 30), 500m);
        Booked(46, "surgeon", Today(15, 0), 45, confirmed: true).Arrive(Today(14, 50));
        Booked(47, "surgeon", Today(19, 0), 45);
        Booked(30, "surgeon", Tomorrow(13, 0), 45, confirmed: true);

        // ——— General practitioners ———
        var gp2Done = Booked(2, "gp2", Today(10, 0), reason: "تلميع", confirmed: true);
        gp2Done.Arrive(Today(9, 50)).Start(Today(10, 2)).Notes(Today(10, 4), "تلميع وتطبيق فلورايد.").Treat(Today(10, 8), "Polishing", "250.00")
            .Finalize(Today(10, 30)).PayAll(Today(10, 34));
        var gp2Busy = Booked(3, "gp2", Today(12, 30), reason: "حشو", confirmed: true);
        gp2Busy.Arrive(Today(12, 25)).Start(Today(12, 33)).Notes(Today(12, 35), "تسوس متوسط العمق في ضرس علوي.")
            .Treat(Today(12, 40), "Filling", "550.00", Teeth("24"), new { filling_type = "Composite", composite_zone = "Posterior" });
        Booked(5, "gp2", Today(18, 0));

        var gp3Done = Booked(8, "gp3", Today(11, 0), reason: "تنظيف", confirmed: true);
        gp3Done.Arrive(Today(10, 55)).Start(Today(11, 3)).Notes(Today(11, 5), "إزالة جير وتصبغات.").Treat(Today(11, 8), "ScalingCleaning", "450.00")
            .Finalize(Today(11, 40)).PayAll(Today(11, 45));
        Booked(29, "gp3", Today(16, 0), confirmed: true);

        Booked(12, "gp4", Today(13, 30)).Arrive(Today(13, 20));
        Booked(10, "gp4", Today(16, 30));

        var gp6Done = Booked(16, "gp6", Today(10, 0), 45, "علاج عصب", confirmed: true);
        gp6Done.Arrive(Today(9, 52)).Start(Today(10, 3)).Notes(Today(10, 5), "علاج عصب ضرس سفلي، جلسة واحدة.").Treat(Today(10, 10), "RootCanal", "2100.00", Teeth("46"))
            .Finalize(Today(10, 50)).Pay(Today(10, 55), 1000m);
        Booked(14, "gp6", Today(17, 0), confirmed: true);
        Booked(22, "gp5", Today(11, 0)).SetStatus(Today(8, 40), "Cancelled", "تعذر الحضور");

        // ——— The days just before: no-show, cancellation, rescheduled to a coming day ———
        Booked(11, "gp4", w.Time.Ago(3, 15, 0)).SetStatus(w.Time.Ago(3, 15, 40), "NoShow");
        Booked(20, "gp4", w.Time.Ago(5, 12, 0)).SetStatus(w.Time.Ago(5, 12, 0).AddHours(-2), "Cancelled", "ظرف طارئ");
        Booked(6, "demo", w.Time.Ago(6, 17, 0)).SetStatus(w.Time.Ago(6, 17, 40), "NoShow");
        Booked(18, "gp6", w.Time.Ago(4, 11, 30), reason: "علاج عصب").Reschedule(w.Time.Ago(4, 11, 30).AddHours(-3), w.Time.Ahead(2, 11, 30), 30, "سفر المريض");

        // ——— Coming days ———
        Booked(1, "gp2", w.Time.Ahead(2, 12, 0), confirmed: true);
        Booked(9, "gp3", w.Time.Ahead(2, 17, 0));
        Booked(13, "gp4", w.Time.Ahead(3, 11, 0), confirmed: true);
        Booked(21, "gp6", w.Time.Ahead(3, 16, 0));
    }
}
