namespace Clinic.Persistence.Migrations;

/// <summary>
/// DEPLOYMENT_ARCHITECTURE.md §2.5: a migration whose operations cannot run in a transaction (CREATE INDEX CONCURRENTLY,
/// VACUUM, CREATE/DROP DATABASE, suppressTransaction). Required exactly when such an operation exists; the migration then
/// contains only those operations, one statement per Sql() call, each idempotent (IF [NOT] EXISTS). Enforced by
/// MigrationSafetyTests.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class NonTransactionalMigrationAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}

/// <summary>
/// DEPLOYMENT_ARCHITECTURE.md §2.5 destructive-operation scan: DROP TABLE / DROP COLUMN / TRUNCATE / DELETE / ALTER COLUMN TYPE /
/// SET NOT NULL on an existing column are refused unless a reviewer adds this marker. The reason must state that the
/// immediately previous release no longer references the dropped or tightened object (expand / contract).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ApprovedDestructiveMigrationAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}
