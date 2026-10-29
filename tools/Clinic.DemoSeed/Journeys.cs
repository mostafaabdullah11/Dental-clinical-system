using System.Globalization;

namespace Clinic.DemoSeed;

/// <summary>One past visit. <c>Ago</c> counts clinic working days before today (scaled to the length of the simulated history).</summary>
internal sealed record R(int P, int Ago, string At, string Doc, string Proc, string Price, string Pay)
{
    public string? Teeth { get; init; }
    public string? Det { get; init; }
    public string? Disc { get; init; }
    public bool DiscBySales { get; init; }
    public string? Fee { get; init; }
    public string? Lab { get; init; }
    public int Mins { get; init; } = 30;
    public bool SameDay { get; init; }
    public bool Confirmed { get; init; }
    public string Method { get; init; } = "Cash";
    public string? Reason { get; init; }
    public int LaterDays { get; init; } = 6;
    public string? Referral { get; init; }
}

/// <summary>The routine clinic life: dozens of finished visits across all Doctors, with every payment situation.</summary>
internal static class Journeys
{
    /// <summary>
    /// Pay: full | none | half | two (half now, the rest later) | over:N (pays N more than owed → Patient Credit) | card | insta.
    /// Proc "" = consultation only (the consultation fee is the only charge).
    /// </summary>
    private static IEnumerable<R> Rows() =>
    [
        // — General practice, Dr. Karim (the demo Doctor account) —
        new(0, 40, "10:00", "demo", "Filling", "350.00", "full") { Teeth = "16", Det = "filling_type=GIC", Confirmed = true },
        new(0, 31, "10:30", "demo", "ScalingCleaning", "450.00", "full") { Reason = "تنظيف دوري" },
        new(1, 39, "11:00", "demo", "RootCanal", "1800.00", "two") { Teeth = "14", Confirmed = true, Fee = "150.00" },
        new(1, 33, "11:00", "demo", "Filling", "900.00", "half") { Teeth = "14", Det = "filling_type=Composite;composite_zone=Posterior" },
        new(2, 38, "12:00", "demo", "Extraction", "300.00", "full") { Teeth = "28", Det = "extraction_type=Simple" },
        new(3, 37, "17:00", "demo", "LaserBleaching", "2800.00", "full") { Det = "quality=Basic", Disc = "300.00", Confirmed = true },
        new(4, 36, "16:00", "demo", "Filling", "700.00", "none") { Teeth = "25", Det = "filling_type=Composite;composite_zone=Anterior", SameDay = true },
        new(5, 35, "13:00", "demo", "", "0.00", "full") { Fee = "200.00", Reason = "كشف واستشارة" },
        new(6, 34, "18:00", "demo", "RootCanal", "2200.00", "half") { Teeth = "36", Disc = "200.00", DiscBySales = true, Confirmed = true },
        new(6, 27, "18:00", "demo", "Filling", "850.00", "full") { Referral = "Prosthetics", Teeth = "36", Det = "filling_type=Composite;composite_zone=Posterior" },
        new(7, 30, "11:30", "demo", "Polishing", "250.00", "card") { Method = "Visa", Reason = "تلميع" },
        new(8, 29, "12:30", "demo", "Extraction", "400.00", "none") { Teeth = "38", Det = "extraction_type=Simple", SameDay = true },
        new(9, 26, "17:30", "demo", "DeepCleaning", "800.00", "insta") { Method = "InstaPay", Confirmed = true },
        new(10, 24, "10:00", "demo", "Filling", "450.00", "over:100") { Teeth = "11", Det = "filling_type=GIC" },
        new(11, 22, "15:00", "demo", "ScalingCleaning", "500.00", "full") { Confirmed = true },
        new(12, 20, "16:30", "demo", "RootCanal", "2400.00", "two") { Referral = "Implant", Teeth = "46", Fee = "150.00" },
        new(13, 18, "11:00", "demo", "Filling", "600.00", "half") { Teeth = "22", Det = "filling_type=Composite;composite_zone=Anterior", Confirmed = true },
        new(14, 16, "13:30", "demo", "Extraction", "350.00", "full") { Teeth = "48", Det = "extraction_type=Simple" },
        new(15, 14, "17:00", "demo", "ScalingCleaning", "450.00", "half"),
        new(16, 12, "10:30", "demo", "Filling", "400.00", "none") { Teeth = "24", Det = "filling_type=GIC", Confirmed = true },
        new(17, 10, "12:00", "demo", "RootCanal", "1900.00", "half") { Teeth = "26", Confirmed = true },
        new(18, 8, "16:00", "demo", "LaserBleaching", "3200.00", "two") { Det = "quality=HighQuality", Disc = "200.00" },
        new(19, 6, "14:00", "demo", "", "0.00", "none") { Fee = "150.00", Reason = "استشارة", SameDay = true },
        new(20, 4, "11:00", "demo", "Filling", "500.00", "full") { Teeth = "15", Det = "filling_type=Composite;composite_zone=Posterior" },
        new(21, 3, "17:30", "demo", "ScalingCleaning", "450.00", "card") { Method = "Visa" },
        new(22, 2, "12:30", "demo", "Extraction", "300.00", "full") { Teeth = "18", Det = "extraction_type=Simple", Confirmed = true },
        new(23, 1, "15:30", "demo", "Polishing", "250.00", "none"),

        // — Oral surgery, Dr. Hisham —
        new(9, 36, "13:00", "surgeon", "Extraction", "1200.00", "half") { Teeth = "38", Det = "extraction_type=Surgical", Mins = 45, Confirmed = true, Reason = "خلع جراحي لضرس العقل" },
        new(24, 32, "14:00", "surgeon", "Extraction", "1800.00", "two") { Teeth = "48", Det = "extraction_type=AdvancedSurgical", Mins = 60 },
        new(25, 25, "14:30", "surgeon", "Extraction", "900.00", "full") { Teeth = "17", Det = "extraction_type=Surgical", Mins = 45, Confirmed = true },
        new(26, 19, "15:00", "surgeon", "Extraction", "1000.00", "half") { Referral = "Implant", Teeth = "28", Det = "extraction_type=Surgical", Mins = 45 },
        new(27, 13, "13:30", "surgeon", "Extraction", "350.00", "full") { Teeth = "35", Det = "extraction_type=Simple" },
        new(7, 9, "16:00", "surgeon", "Extraction", "1500.00", "none") { Teeth = "48", Det = "extraction_type=AdvancedSurgical", Mins = 60, Confirmed = true },
        new(12, 5, "14:00", "surgeon", "Extraction", "950.00", "full") { Teeth = "38", Det = "extraction_type=Surgical", Mins = 45 },

        // — Orthodontics clinic: routine care (the paid Orthodontic cases are in the Orthodontic stories) —
        new(14, 21, "16:00", "ortho", "ScalingCleaning", "500.00", "full"),
        new(18, 15, "17:00", "ortho", "Polishing", "300.00", "full") { Confirmed = true },
        new(3, 7, "15:30", "ortho", "ScalingCleaning", "500.00", "half"),

        // — General practitioners —
        new(1, 38, "09:30", "gp2", "ScalingCleaning", "450.00", "full") { Confirmed = true },
        new(2, 32, "10:00", "gp2", "Filling", "400.00", "full") { Teeth = "21", Det = "filling_type=GIC" },
        new(10, 28, "11:30", "gp2", "RootCanal", "2100.00", "two") { Referral = "Other", Teeth = "47", Confirmed = true, Fee = "100.00" },
        new(11, 23, "16:30", "gp2", "Filling", "650.00", "none") { Teeth = "12", Det = "filling_type=Composite;composite_zone=Anterior" },
        new(16, 17, "17:00", "gp2", "ScalingCleaning", "450.00", "full"),
        new(19, 11, "10:30", "gp2", "RootCanal", "1700.00", "full") { Teeth = "34", Confirmed = true },
        new(20, 6, "12:00", "gp2", "Filling", "500.00", "half") { Teeth = "13", Det = "filling_type=GIC" },
        new(22, 2, "16:00", "gp2", "Polishing", "250.00", "full"),
        new(2, 5, "15:30", "gp2", "ScalingCleaning", "450.00", "full") { Confirmed = true },

        new(3, 39, "13:30", "gp3", "Extraction", "300.00", "full") { Teeth = "18", Det = "extraction_type=Simple" },
        new(4, 34, "14:30", "gp3", "RootCanal", "2000.00", "half") { Teeth = "45", Confirmed = true },
        new(8, 30, "17:30", "gp3", "Filling", "750.00", "full") { Teeth = "27", Det = "filling_type=Composite;composite_zone=Posterior" },
        new(12, 25, "18:30", "gp3", "ScalingCleaning", "500.00", "card") { Method = "Visa" },
        new(21, 19, "13:00", "gp3", "Filling", "550.00", "none") { Teeth = "32", Det = "filling_type=Composite;composite_zone=Anterior" },
        new(23, 14, "11:00", "gp3", "RootCanal", "2300.00", "two") { Teeth = "16", Confirmed = true },
        new(24, 9, "16:00", "gp3", "DeepCleaning", "850.00", "full"),
        new(5, 4, "12:30", "gp3", "Filling", "400.00", "full") { Teeth = "36", Det = "filling_type=GIC", Confirmed = true },

        new(5, 37, "09:30", "gp4", "Filling", "450.00", "full") { Teeth = "44", Det = "filling_type=GIC" },
        new(9, 33, "12:00", "gp4", "ScalingCleaning", "450.00", "insta") { Method = "InstaPay", Confirmed = true },
        new(13, 27, "15:00", "gp4", "LaserBleaching", "2600.00", "two") { Det = "quality=Basic" },
        new(14, 22, "10:30", "gp4", "Extraction", "400.00", "none") { Teeth = "47", Det = "extraction_type=Simple", SameDay = true },
        new(15, 16, "17:00", "gp4", "RootCanal", "1800.00", "full") { Teeth = "15", Confirmed = true },
        new(25, 10, "11:30", "gp4", "Filling", "480.00", "full") { Teeth = "23", Det = "filling_type=GIC" },
        new(26, 3, "14:30", "gp4", "ScalingCleaning", "450.00", "half"),

        new(6, 36, "10:00", "gp5", "ScalingCleaning", "450.00", "full"),
        new(11, 31, "13:00", "gp5", "Filling", "420.00", "full") { Teeth = "31", Det = "filling_type=GIC" },
        new(16, 26, "16:00", "gp5", "RootCanal", "1750.00", "half") { Referral = "Surgery", Teeth = "36" },
        new(17, 21, "11:00", "gp5", "Extraction", "350.00", "full") { Teeth = "38", Det = "extraction_type=Simple", Confirmed = true },
        new(22, 17, "17:00", "gp5", "Filling", "600.00", "none") { Teeth = "25", Det = "filling_type=Composite;composite_zone=Anterior" },
        new(27, 12, "12:30", "gp5", "ScalingCleaning", "450.00", "full"),

        new(8, 35, "11:00", "gp6", "Filling", "380.00", "full") { Teeth = "37", Det = "filling_type=GIC" },
        new(13, 29, "15:30", "gp6", "ScalingCleaning", "450.00", "full") { Confirmed = true },
        new(18, 24, "13:30", "gp6", "RootCanal", "2000.00", "two") { Referral = "Orthodontics", Teeth = "11", Fee = "120.00" },
        new(21, 18, "16:30", "gp6", "Polishing", "250.00", "full"),
        new(23, 8, "18:00", "gp6", "Filling", "700.00", "none") { Teeth = "24", Det = "filling_type=Composite;composite_zone=Anterior", Confirmed = true },
        new(24, 2, "10:30", "gp6", "ScalingCleaning", "450.00", "full"),

        // — Children (Pediatric procedures, primary-tooth numbering) —
        new(28, 30, "16:00", "gp3", "PediatricPulpotomy", "600.00", "half") { Teeth = "75", Confirmed = true, Reason = "ألم في ضرس لبني" },
        new(29, 24, "17:00", "gp4", "PediatricStainlessSteelCrown", "500.00", "half") { Teeth = "85" },
        new(30, 20, "15:00", "gp2", "PediatricExtraction", "250.00", "full") { Teeth = "62" },
        new(31, 15, "16:30", "gp6", "PediatricSpaceMaintainer", "700.00", "two") { Teeth = "74", Confirmed = true },
        new(32, 9, "15:00", "demo", "PediatricPulpectomy", "800.00", "none") { Teeth = "84" },
        new(28, 4, "16:30", "gp3", "PediatricZirconCrown", "900.00", "full") { Teeth = "75", Confirmed = true },
    ];

    private static readonly Dictionary<string, string> NotesByProcedure = new()
    {
        ["Filling"] = "تم تحضير التجويف وإزالة التسوس وعمل الحشو. الحالة مستقرة.",
        ["RootCanal"] = "تم فتح الحجرة اللبية وتنظيف القنوات وحشوها. يُنصح بتغطية الضرس بتاج.",
        ["Extraction"] = "خلع تحت التخدير الموضعي بدون مضاعفات. تعليمات ما بعد الخلع.",
        ["ScalingCleaning"] = "إزالة الجير والتصبغات وتلميع. نصح بالعناية بالفرشاة والخيط.",
        ["DeepCleaning"] = "تنظيف عميق تحت اللثة. موعد متابعة بعد أسبوعين.",
        ["Polishing"] = "تلميع الأسنان وتطبيق الفلورايد.",
        ["LaserBleaching"] = "جلسة تبييض بالليزر. تجنب المشروبات الملونة لمدة 48 ساعة.",
    };

    public static void Register(Plan plan, World w, int maxAgo)
    {
        var rowNo = 0;
        var rows = Rows().ToList();
        var firstVisit = rows.GroupBy(r => r.P).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Ago).First());
        foreach (var row in rows)
        {
            var ago = Scale(row.Ago, maxAgo);
            var visitDate = w.Time.WorkdaysAgo(ago);
            var (hour, minute) = ParseTime(row.At);
            var start = w.Time.At(visitDate, hour, minute);
            var sameDay = row.SameDay || ago + 1 > maxAgo;
            var bookAt = sameDay ? start.AddMinutes(-45) : w.Time.At(w.Time.WorkdaysAgo(ago + 1), 12, 5 + rowNo % 40);
            w.NeedPatient(row.P, bookAt.AddMinutes(-30));
            rowNo++;

            var v = new VisitScript(plan, w, row.P, row.Doc, PatientStories.SalesFor(row.P));
            v.Book(bookAt, start, row.Mins, row.Reason ?? "كشف وعلاج");
            if (row.Confirmed)
            {
                v.Confirm(sameDay ? start.AddMinutes(-30) : w.Time.At(visitDate, 8, 0).AddMinutes(rowNo % 20));
            }

            v.Arrive(start.AddMinutes(-10)).Start(start.AddMinutes(2));
            if (ReferenceEquals(firstVisit[row.P], row))
            {
                v.ChiefComplaint(start.AddMinutes(3), ComplaintFor(row.Proc, row.P));
            }

            v.Notes(start.AddMinutes(4), NotesByProcedure.GetValueOrDefault(row.Proc, "كشف سريري وتقييم الحالة وشرح الخطة العلاجية للمريض."), row.Fee);

            var offset = 6;
            if (row.Proc.Length > 0)
            {
                v.Treat(start.AddMinutes(offset++), row.Proc, row.Price, row.Teeth?.Split(','), Details(row.Det));
                if (row.Disc is not null)
                {
                    v.Discount(start.AddMinutes(offset++), row.Disc, row.DiscBySales);
                }

                if (row.Lab is not null)
                {
                    v.FixedLab(start.AddMinutes(offset++), row.Lab);
                }

                offset = ClinicalExtras(v, row, start, offset);
            }

            var end = start.AddMinutes(Math.Max(row.Mins, offset + 2));
            v.Finalize(end);

            var total = Money(row.Price) - Money(row.Disc) + Money(row.Fee);
            var payAt = end.AddMinutes(6);
            switch (row.Pay.Split(':')[0])
            {
                case "full":
                case "card":
                case "insta":
                    v.PayAll(payAt, row.Method);
                    break;
                case "half":
                    v.Pay(payAt, HalfOf(total), row.Method);
                    break;
                case "two":
                    v.Pay(payAt, HalfOf(total), row.Method);
                    v.PayAll(w.Time.At(w.Time.WorkdaysAgo(Math.Max(0, ago - row.LaterDays)), 12, 20 + rowNo % 30), row.Method);
                    break;
                case "over":
                    v.Pay(payAt, total + Money(row.Pay.Split(':')[1]), row.Method);
                    break;
            }
        }

    }

    /// <summary>Root canal measurements and steps, prescriptions and referrals — clinical records only the Doctor (and Manager reports) see.</summary>
    private static readonly Dictionary<string, string[]> Complaints = new()
    {
        ["Filling"] = ["ألم عند تناول الحلويات والبارد في ضرس", "تسوس ظاهر ويحتاج حشو"],
        ["RootCanal"] = ["ألم شديد ليلاً يوقظ من النوم", "ألم نابض عند الضغط على الضرس"],
        ["Extraction"] = ["ضرس متكسر ومؤلم يرغب في خلعه", "ضرس عقل يسبب التهاباً متكرراً"],
        ["ScalingCleaning"] = ["تراكم جير ورائحة فم", "رغبة في تنظيف الأسنان دورياً"],
        ["LaserBleaching"] = ["رغبة في تفتيح لون الأسنان"],
        ["DeepCleaning"] = ["نزيف اللثة أثناء التفريش"],
        ["Polishing"] = ["تصبغات سطحية"],
    };

    private static string ComplaintFor(string procedure, int patient) =>
        Complaints.TryGetValue(procedure, out var options) ? options[patient % options.Length] : procedure.StartsWith("Pediatric", StringComparison.Ordinal) ? "ألم في ضرس لبني عند الطفل" : "كشف واستشارة";

    private static int ClinicalExtras(VisitScript v, R row, DateTimeOffset start, int offset)
    {
        if (row.Proc == "RootCanal")
        {
            var tooth = row.Teeth?.Split(',')[0] ?? "16";
            var canals = tooth[^1] switch
            {
                '6' or '7' or '8' => new[] { (1, "20.50"), (2, "19.00"), (3, "21.00") },
                '4' or '5' => new[] { (1, "21.50"), (2, "20.00") },
                _ => new[] { (1, "22.00") },
            };
            v.Canals(start.AddMinutes(offset++), canals);
            v.Step(start.AddMinutes(offset++), "AccessCavity").Step(start.AddMinutes(offset++), "MechanicalPreparation");
            if (row.Pay is "full" or "card" or "insta")
            {
                v.Step(start.AddMinutes(offset++), "Obturation");
            }

            v.Prescribe(start.AddMinutes(offset++), "بعد الأكل", ("Ibuprofen 400mg", "قرص", "عند الألم", "3 أيام"));
        }
        else if (row.Proc == "Extraction")
        {
            v.Prescribe(start.AddMinutes(offset++), "بعد الأكل مع الراحة التامة", ("Amoxicillin 500mg", "كبسولة", "3 مرات يومياً", "5 أيام"), ("Ibuprofen 400mg", "قرص", "عند الألم", "3 أيام"));
        }

        if (row.Referral is not null)
        {
            var (reason, notes, instructions) = row.Referral switch
            {
                "Implant" => ("فقد ضرس ويحتاج تقييم زراعة", "تقييم العظم المتاح مطلوب قبل تحديد نوع الزراعة.", "الحجز خلال أسبوعين"),
                "Prosthetics" => ("حاجة إلى تركيب ثابت", "الضرس المعالج يحتاج تاجاً بعد استقرار الحالة.", "تحديد موعد بعد شهر"),
                "Orthodontics" => ("ازدحام بالأسنان الأمامية", "يُنصح بتقييم تقويمي قبل استكمال العلاج التجميلي.", "الحجز في عيادة التقويم"),
                "Surgery" => ("ضرس عقل مدفون", "يظهر ميل واضح في الأشعة الخارجية للمريض.", "التنسيق مع جراح الفم"),
                _ => ("استشارة تخصصية إضافية", "حالة تحتاج رأياً ثانياً.", (string?)null),
            };
            v.Referral(start.AddMinutes(offset++), row.Referral, reason, notes, instructions);
        }

        return offset;
    }

    internal static int Scale(int ago, int maxAgo)
    {
        if (ago == 0)
        {
            return 0;
        }

        // The table is written for 40 working days of history; shorter histories are compressed proportionally.
        var usable = Math.Max(2, maxAgo - 2);
        return Math.Clamp((int)Math.Round(ago * usable / 40.0, MidpointRounding.AwayFromZero), 1, usable);
    }

    private static (int Hour, int Minute) ParseTime(string text)
    {
        var parts = text.Split(':');
        return (int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private static decimal Money(string? text) => text is null ? 0m : decimal.Parse(text, CultureInfo.InvariantCulture);

    private static decimal HalfOf(decimal total) => Math.Floor(total / 2 / 50m) * 50m;

    /// <summary>"a=b;c=d" → { a: "b", c: "d" } (the procedure's option fields).</summary>
    private static Dictionary<string, string>? Details(string? text) => text?.Split(';')
        .Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1]);
}
