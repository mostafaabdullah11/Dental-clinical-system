using Clinic.Modules.Scheduling.Contracts;
using Clinic.Modules.Scheduling.Domain;
using Clinic.Modules.Scheduling.Features;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Scheduling.Infrastructure;

/// <summary>
/// FR-SCH-001 / FR-SCH-002: weekly Doctor availability. Ranges are withdrawn, never deleted; live ranges of one Doctor
/// may not overlap (exclusion constraint). Availability is guidance for booking: appointments may still overlap.
/// </summary>
internal sealed class AvailabilityService(DbContext db, IAuditWriter audit, ICurrentActor actor, IClock clock, IDoctorDirectory doctors)
{
    private const string OverlapConstraint = "ex_availability_ranges_no_overlap";

    public async Task<AvailabilityWeekDto> GetWeekAsync(Guid doctorId, DateOnly weekStart, CancellationToken ct)
    {
        var week = await db.Set<AvailabilityWeek>().AsNoTracking().SingleOrDefaultAsync(w => w.DoctorId == doctorId && w.WeekStartDate == weekStart, ct);
        if (week is null)
        {
            return new AvailabilityWeekDto(doctorId, weekStart, "None", null, [], null);
        }

        return new AvailabilityWeekDto(doctorId, weekStart, week.Status, week.PublishedAt, await LiveRangesAsync(week.Id, ct), ETags.Format(week.RowVersion));
    }

    public async Task<AvailabilityWeekDto> SaveWeekAsync(Guid doctorId, DateOnly weekStart, IReadOnlyList<AvailabilityRangeInput> ranges, string? ifMatch,
        CancellationToken ct)
    {
        if (await doctors.FindAsync(doctorId, ct) is null)
        {
            throw AppException.NotFound();
        }

        if (ranges.Any(r => r.Date < weekStart || r.Date > weekStart.AddDays(6)))
        {
            throw AppException.Validation("ranges", FieldErrorCodes.OutOfRange);
        }

        var ordered = ranges.OrderBy(r => r.Date).ThenBy(r => r.Start).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Date == ordered[i - 1].Date && ordered[i].Start < ordered[i - 1].End)
            {
                throw AppException.Coded(409, SchedulingErrors.AvailabilityOverlap);
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var week = await db.Set<AvailabilityWeek>()
            .FromSqlInterpolated($"SELECT w.*, w.xmin FROM scheduling.availability_weeks w WHERE w.doctor_id = {doctorId} AND w.week_start_date = {weekStart} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (week is null)
        {
            week = new AvailabilityWeek
            {
                Id = Guid.CreateVersion7(), DoctorId = doctorId, WeekStartDate = weekStart, Status = AvailabilityWeekStatuses.Draft, CreatedAt = clock.UtcNow,
            };
            db.Set<AvailabilityWeek>().Add(week);
        }
        else
        {
            AppointmentCommands.RequireIfMatch(ifMatch, week.RowVersion);
            var now = clock.UtcNow;
            foreach (var live in await db.Set<AvailabilityRange>().Where(r => r.WeekId == week.Id && r.WithdrawnAt == null).ToListAsync(ct))
            {
                live.WithdrawnAt = now;
            }
        }

        await db.SaveChangesAsync(ct);
        foreach (var range in ordered)
        {
            db.Set<AvailabilityRange>().Add(new AvailabilityRange
            {
                Id = Guid.CreateVersion7(), WeekId = week.Id, DoctorId = doctorId, DayDate = range.Date, StartTime = range.Start, EndTime = range.End,
            });
        }

        audit.Add(new AuditEvent("scheduling.availability.saved", "scheduling.availability_weeks", week.Id.ToString(),
            NewValue: new { doctorId, weekStart, ranges = ordered.Count }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (DbErrors.IsExclusionViolation(e, OverlapConstraint))
        {
            throw AppException.Coded(409, SchedulingErrors.AvailabilityOverlap);
        }

        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
        return await GetWeekAsync(doctorId, weekStart, ct);
    }

    public async Task<AvailabilityWeekDto> PublishAsync(Guid doctorId, DateOnly weekStart, string? ifMatch, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var week = await db.Set<AvailabilityWeek>()
            .FromSqlInterpolated($"SELECT w.*, w.xmin FROM scheduling.availability_weeks w WHERE w.doctor_id = {doctorId} AND w.week_start_date = {weekStart} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw AppException.NotFound();
        if (week.Status != AvailabilityWeekStatuses.Published)
        {
            AppointmentCommands.RequireIfMatch(ifMatch, week.RowVersion);
            week.Status = AvailabilityWeekStatuses.Published;
            week.PublishedAt = clock.UtcNow;
            week.PublishedByUserId = actor.UserId;
            audit.Add(new AuditEvent("scheduling.availability.published", "scheduling.availability_weeks", week.Id.ToString(),
                NewValue: new { doctorId, weekStart }));
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
        return await GetWeekAsync(doctorId, weekStart, ct);
    }

    /// <summary>Live ranges of the Doctor's published weeks between two dates (Sales booking guidance, Doctor schedule).</summary>
    public async Task<IReadOnlyList<AvailabilityRangeDto>> PublishedRangesAsync(Guid doctorId, DateOnly firstDay, DateOnly lastDay, CancellationToken ct) =>
        await (from r in db.Set<AvailabilityRange>().AsNoTracking()
               join w in db.Set<AvailabilityWeek>().AsNoTracking() on r.WeekId equals w.Id
               where r.DoctorId == doctorId && r.WithdrawnAt == null && w.Status == AvailabilityWeekStatuses.Published
                     && r.DayDate >= firstDay && r.DayDate <= lastDay
               orderby r.DayDate, r.StartTime
               select new AvailabilityRangeDto(r.Id, r.DayDate, r.StartTime, r.EndTime)).ToListAsync(ct);

    public async Task<IReadOnlyList<BookedRangeDto>> BookedAsync(Guid doctorId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        await db.Set<EffectiveBookedRange>().AsNoTracking()
            .Where(b => b.DoctorId == doctorId && b.StartAt < to && b.EffectiveEndAt > from)
            .OrderBy(b => b.StartAt)
            .Select(b => new BookedRangeDto(b.AppointmentId, b.StartAt, b.EffectiveEndAt)).ToListAsync(ct);

    private async Task<IReadOnlyList<AvailabilityRangeDto>> LiveRangesAsync(Guid weekId, CancellationToken ct) =>
        await db.Set<AvailabilityRange>().AsNoTracking().Where(r => r.WeekId == weekId && r.WithdrawnAt == null)
            .OrderBy(r => r.DayDate).ThenBy(r => r.StartTime)
            .Select(r => new AvailabilityRangeDto(r.Id, r.DayDate, r.StartTime, r.EndTime)).ToListAsync(ct);
}

internal static class AppointmentQueries
{
    public static IQueryable<Appointment> Between(this IQueryable<Appointment> query, DateTimeOffset? from, DateTimeOffset? to) =>
        query.Where(a => (from == null || a.StartAt >= from) && (to == null || a.StartAt < to));

    public static bool IsOpen(this Appointment appointment) => AppointmentStatuses.IsOpen(appointment.Status);
}
