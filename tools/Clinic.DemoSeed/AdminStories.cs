using System.Text.Json;

namespace Clinic.DemoSeed;

/// <summary>
/// The Manager's back office: operational expenses (the 11 categories), attendance for Assistants and Sales, approved Assistant
/// overtime, and last month's payroll through the real draft → adjust → finalize → paid lifecycle.
/// </summary>
internal static class AdminStories
{
    private const string Water = "مياه", Electricity = "كهرباء", Rent = "إيجار", Internet = "إنترنت", Maintenance = "صيانة", Cleaning = "نظافة",
        Office = "أدوات مكتبية", Medical = "خامات / مستلزمات طبية", Marketing = "تسويق", Transport = "مواصلات", Petty = "نثريات / متفرقات";

    public static void Register(Plan plan, World w)
    {
        RegisterExpenses(plan, w);
        RegisterAttendance(plan, w);
        RegisterPayroll(plan, w);
    }

    private static IEnumerable<DateOnly> WorkingDays(World w, DateOnly from, DateOnly to)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek != DayOfWeek.Friday)
            {
                yield return d;
            }
        }
    }

    private static void RegisterExpenses(Plan plan, World w)
    {
        var first = w.Time.FirstOfMonth(1);
        var months = new[] { first, w.Time.FirstOfMonth(0) };

        foreach (var month in months)
        {
            // Fixed monthly costs and the usual supplies for the month; only days that already happened are written.
            var items = new (int Day, string Category, string Description, string Amount, string Method, string? Vendor)[]
            {
                (1, Rent, "إيجار مقر العيادة", "9500.00", "InstaPay", "مالك العقار"),
                (3, Internet, "اشتراك الإنترنت الشهري", "450.00", "Visa", "شركة الاتصالات"),
                (5, Electricity, "فاتورة الكهرباء", "1380.00", "Cash", null),
                (6, Water, "فاتورة المياه", "210.00", "Cash", null),
                (7, Medical, "مستلزمات حشو وتخدير", "2750.00", "Cash", "شركة المستلزمات الطبية"),
                (9, Cleaning, "خدمة النظافة الأسبوعية", "600.00", "Cash", null),
                (11, Maintenance, "صيانة جهاز الأشعة السطحي للتعقيم", "800.00", "Cash", "فني الصيانة"),
                (13, Marketing, "إعلان ممول على وسائل التواصل", "1500.00", "Visa", null),
                (15, Office, "ورق وطابعة وأدوات مكتبية", "380.00", "Cash", null),
                (17, Transport, "مصاريف انتقال ونقل عينات المعمل", "260.00", "Cash", null),
                (19, Medical, "قفازات وكمامات ومعقمات", "1150.00", "Cash", "شركة المستلزمات الطبية"),
                (21, Cleaning, "مواد تنظيف وتعقيم", "340.00", "Cash", null),
                (24, Petty, "ضيافة ونثريات متنوعة", "190.00", "Cash", null),
            };
            foreach (var item in items)
            {
                var date = month.AddDays(item.Day - 1);
                if (date >= w.Time.Today)
                {
                    continue;
                }

                plan.At(w.Time.At(date, 14, item.Day % 50), $"expense {item.Description}", async () =>
                {
                    await w.Manager.PostAsync("/api/manager/expenses", new
                    {
                        expenseDate = date.ToString("yyyy-MM-dd"), categoryId = w.ExpenseCategories[item.Category], description = item.Description,
                        amount = item.Amount, paymentMethodCode = item.Method, paidToVendor = item.Vendor, notes = (string?)null,
                    });
                    w.Bump("expenses");
                });
            }
        }

        // Sales / Reception may record only the two operational categories (Materials / Petty).
        foreach (var (ago, key, category, spec, amount) in new[]
                 {
                     (20, "demo", Medical, "قفازات وكمامات", "350.00"),
                     (13, "two", Petty, "مشروبات للضيافة", "120.00"),
                     (7, "demo", Medical, "خيوط جراحية ومواد تعقيم", "620.00"),
                     (2, "two", Petty, "أدوات صغيرة متنوعة", "85.00"),
                 })
        {
            plan.At(w.Time.Ago(ago, 15, 40), $"sales expense {spec}", async () =>
            {
                await w.SalesSession(key).PostAsync("/api/sales/expenses", new { categoryId = w.ExpenseCategories[category], amount, specification = spec });
                w.Bump("expenses (recorded by Sales)");
            });
        }

        // One audited Manager edit of an expense.
        plan.At(w.Time.Ago(1, 12, 0), "edit an expense", async () =>
        {
            var today = w.Time.Today.ToString("yyyy-MM-dd");
            var list = await w.Manager.GetAsync($"/api/manager/expenses?from={w.Time.FirstOfMonth(1):yyyy-MM-dd}&to={today}");
            var row = list.Items().First(e => e.Str("description") == "ورق وطابعة وأدوات مكتبية");
            await w.Manager.PutAsync($"/api/manager/expenses/{row.Id()}", new
            {
                expenseDate = row.Str("expenseDate"), categoryId = w.ExpenseCategories[Office], description = "ورق وحبر وأدوات مكتبية", amount = "410.00",
                paymentMethodCode = "Cash", paidToVendor = (string?)null, notes = "تم تعديل المبلغ بعد مراجعة الفاتورة",
            }, row.Ver());
            w.Bump("expense edits");
        });
    }

    private static void RegisterAttendance(Plan plan, World w)
    {
        var from = w.Time.FirstOfMonth(1);
        var yesterday = w.Time.Today.AddDays(-1);
        var overtimeDays = new HashSet<DateOnly>();
        var dayNo = 0;
        foreach (var day in WorkingDays(w, from.AddDays(1), yesterday))
        {
            var number = dayNo++;
            if (number % 5 == 2)
            {
                overtimeDays.Add(day);
            }

            plan.At(w.Time.At(day, 18, 30), $"attendance {day:MM-dd}", async () =>
            {
                var assistantIndex = 0;
                foreach (var (_, assistantId) in w.Assistants)
                {
                    assistantIndex++;
                    // Mostly present; a few late arrivals and absences spread over the staff.
                    var status = (number + assistantIndex) % 17 == 0 ? "Absent" : (number + assistantIndex) % 7 == 0 ? "Late" : "Present";
                    var record = await w.Manager.PostAsync("/api/manager/staff/attendance", new
                    {
                        assistantId, salesEmployeeId = (Guid?)null, date = day.ToString("yyyy-MM-dd"), status,
                        checkInTime = status == "Absent" ? null : status == "Late" ? "09:40:00" : "09:00:00",
                        checkOutTime = status == "Absent" ? null : "17:00:00", notes = (string?)null,
                    });
                    w.Bump("attendance records");

                    // Approved overtime is a Manager decision, never inferred from the times above.
                    var overtimeRate = assistantIndex <= 3;
                    if (status != "Absent" && overtimeDays.Contains(day) && overtimeRate && assistantIndex == 1 + number % 3)
                    {
                        await w.Manager.PostAsync($"/api/manager/staff/assistants/{assistantId}/overtime", new
                        {
                            workDate = day.ToString("yyyy-MM-dd"), approvedHours = number % 2 == 0 ? "2.00" : "1.50", attendanceRecordId = record.Id(), notes = "تجهيز الغرف بعد ساعات العمل",
                        });
                        w.Bump("overtime entries");
                    }
                }

                var salesIndex = 0;
                foreach (var key in new[] { "demo", "two", "three" })
                {
                    salesIndex++;
                    var status = (number + salesIndex * 3) % 19 == 0 ? "Absent" : (number + salesIndex) % 8 == 0 ? "Late" : "Present";
                    await w.Manager.PostAsync("/api/manager/staff/attendance", new
                    {
                        assistantId = (Guid?)null, salesEmployeeId = w.SalesStaff(key).StaffId, date = day.ToString("yyyy-MM-dd"), status,
                        checkInTime = status == "Absent" ? null : status == "Late" ? "10:25:00" : "09:55:00",
                        checkOutTime = status == "Absent" ? null : "18:00:00", notes = (string?)null,
                    });
                    w.Bump("attendance records");
                }
            });
        }
    }

    /// <summary>Last month's payroll, created once the month has ended (a Draft before that could not be finalized).</summary>
    private static void RegisterPayroll(Plan plan, World w)
    {
        var previous = w.Time.FirstOfMonth(1);
        var month = previous.ToString("yyyy-MM");
        var at = w.Time.FirstOfMonth(0).AddDays(1);
        if (at > w.Time.Today)
        {
            at = w.Time.Today;
        }

        var start = w.Time.At(at, 10, 0);

        plan.At(start, "payroll: sales", async () =>
        {
            var index = 0;
            foreach (var key in new[] { "demo", "two", "three" })
            {
                var draft = await w.Manager.PostAsync("/api/manager/payroll/sales/drafts", new { salesEmployeeId = w.SalesStaff(key).StaffId, month });
                if (index == 0)
                {
                    await w.Manager.PostAsync($"/api/manager/payroll/sales/{draft.Id()}/adjustments", new { kind = "Bonus", amount = "250.00", reason = "تميز في المبيعات" });
                }

                if (index == 1)
                {
                    await w.Manager.PostAsync($"/api/manager/payroll/sales/{draft.Id()}/adjustments", new { kind = "Deduction", amount = "100.00", reason = "تأخير متكرر" });
                }

                if (index < 2)
                {
                    await w.Manager.PostAsync($"/api/manager/payroll/sales/{draft.Id()}/finalize");
                    if (index == 0)
                    {
                        await w.Manager.PostAsync($"/api/manager/payroll/sales/{draft.Id()}/mark-paid");
                    }
                }

                index++;
                w.Bump("payroll sheets (sales)");
            }
        });

        plan.At(start.AddMinutes(10), "payroll: doctors", async () =>
        {
            var index = 0;
            foreach (var key in new[] { "demo", "surgeon", "ortho", "gp2", "gp3", "gp4" })
            {
                var draft = await w.Manager.PostAsync("/api/manager/payroll/doctors/drafts", new { doctorId = w.Doctor(key).StaffId, month });
                if (index == 0)
                {
                    await w.Manager.PostAsync($"/api/manager/payroll/doctors/{draft.Id()}/adjustments", new { amount = "300.00", reason = "حافز الأداء الشهري" });
                }

                if (index < 4)
                {
                    await w.Manager.PostAsync($"/api/manager/payroll/doctors/{draft.Id()}/finalize");
                    if (index < 2)
                    {
                        await w.Manager.PostAsync($"/api/manager/payroll/doctors/{draft.Id()}/mark-paid");
                    }
                }

                index++;
                w.Bump("payroll sheets (doctors)");
            }
        });

        plan.At(start.AddMinutes(20), "payroll: assistants", async () =>
        {
            var index = 0;
            foreach (var (_, assistantId) in w.Assistants)
            {
                var draft = await w.Manager.PostAsync("/api/manager/payroll/assistants/drafts", new { assistantId, month });
                if (index < 3)
                {
                    await w.Manager.PostAsync($"/api/manager/payroll/assistants/{draft.Id()}/finalize");
                    if (index < 2)
                    {
                        await w.Manager.PostAsync($"/api/manager/payroll/assistants/{draft.Id()}/mark-paid");
                    }
                }

                index++;
                w.Bump("payroll sheets (assistants)");
            }
        });
    }
}
