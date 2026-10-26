using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Clinic.SharedKernel.Persistence;

public static class DbErrors
{
    /// <summary>True when the save failed on the named PostgreSQL unique constraint.</summary>
    public static bool IsUniqueViolation(DbUpdateException exception, string constraintName) =>
        exception.InnerException is PostgresException pg
        && pg.SqlState == PostgresErrorCodes.UniqueViolation
        && pg.ConstraintName == constraintName;

    /// <summary>True when the save failed on the named PostgreSQL exclusion constraint.</summary>
    public static bool IsExclusionViolation(DbUpdateException exception, string constraintName) =>
        exception.InnerException is PostgresException pg
        && pg.SqlState == PostgresErrorCodes.ExclusionViolation
        && pg.ConstraintName == constraintName;
}
