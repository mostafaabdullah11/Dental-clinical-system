using System.Diagnostics;
using Clinic.IntegrationTests.Infrastructure;
using Clinic.Persistence.Operations;
using Npgsql;
using Xunit;

namespace Clinic.IntegrationTests.Platform;

/// <summary>
/// F5.22–F5.24 / DEPLOYMENT_ARCHITECTURE.md §3.3–§3.4, §3.7 #1, #3, #4, #6, #16–#18: the restricted clinic_backup role produces a
/// complete dump that restores (as clinic_migrator) with identical manifest, triggers, grants and passing integrity checks;
/// Data Protection key rows are never dumped; an injected ledger violation fails the integrity check.
/// Uses the real PostgreSQL client tools (pg_dump / pg_restore on PATH or in CLINIC_PG_BIN).
/// </summary>
public sealed class BackupRestoreDrillTests(ApiFixture fixture)
{
    [Fact]
    public async Task Integrity_checks_pass_on_the_live_database_as_app_and_backup_roles()
    {
        foreach (var role in new[] { "clinic_app", "clinic_backup" })
        {
            await using var connection = new NpgsqlConnection(fixture.Database.ConnectionFor(role));
            var results = await OperationalChecks.RunIntegrityAsync(connection, CancellationToken.None);
            Assert.True(results.Count >= 10);
            Assert.All(results, r => Assert.True(r.Violations == 0, $"{role}: {r.Check} = {r.Violations}"));
        }
    }

    [Fact] // §3.7 #4: an injected ledger violation (constraints bypassed on a throwaway database) fails the check
    public async Task Injected_ledger_violation_fails_the_integrity_check()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.ExecuteAsync("""
            SET session_replication_role = replica;
            INSERT INTO patients.patients (id, full_name, date_of_birth, registered_at, is_active, created_at)
            VALUES ('00000000-0000-0000-0000-00000000aa01', 'اختبار', '1990-01-01', now(), true, now());
            INSERT INTO finance.payments (id, patient_id, amount, kind, payment_method_code, paid_at, received_by_user_id)
            VALUES (gen_random_uuid(), '00000000-0000-0000-0000-00000000aa01', 100, 'Receipt', 'Cash', now(), gen_random_uuid());
            INSERT INTO finance.patient_credit_entries (id, patient_id, payment_id, entry_kind, amount, created_by_user_id, created_at)
            SELECT gen_random_uuid(), patient_id, id, 'Reversal', -50, received_by_user_id, now() FROM finance.payments;
            """);
        await using var connection = new NpgsqlConnection(database.ConnectionFor("clinic_backup"));
        var results = (await OperationalChecks.RunIntegrityAsync(connection, CancellationToken.None)).ToDictionary(r => r.Check, r => r.Violations);
        Assert.Equal(1, results["g8_payment_conservation"]);
        Assert.Equal(1, results["negative_patient_credit"]);
    }

    [Fact] // §2.2 migrations job / §3.7 #19: every table and sequence has a backup-grant decision (SELECT), except the key ring
    public async Task Every_table_and_sequence_has_a_backup_grant_decision()
    {
        Assert.Empty(await fixture.Database.ColumnAsync("""
            SELECT t.table_schema || '.' || t.table_name FROM information_schema.tables t
            WHERE t.table_type = 'BASE TABLE'
              AND t.table_schema IN ('audit','clinical','configuration','crm','expenses','feedback','finance','identity','infra','notifications',
                                     'orthodontics','patients','payroll','prescriptions','scheduling','staff')
              AND NOT (t.table_schema = 'infra' AND t.table_name = 'data_protection_keys')
              AND NOT has_table_privilege('clinic_backup', format('%I.%I', t.table_schema, t.table_name), 'SELECT')
            """));
        Assert.Empty(await fixture.Database.ColumnAsync("""
            SELECT n.nspname || '.' || c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind = 'S' AND n.nspname NOT IN ('pg_catalog', 'information_schema', 'infra')
              AND NOT CASE WHEN c.relkind = 'S' THEN has_sequence_privilege('clinic_backup', c.oid, 'SELECT') ELSE true END
            """));
    }

    [Fact] // §3.7 #3 / #17: clinic_backup reads what it was granted, writes nothing, never reads the key table
    public async Task Backup_role_is_read_only_and_cannot_read_data_protection_keys()
    {
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("SELECT 1 FROM infra.data_protection_keys", "clinic_backup"));
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>(
            "SELECT count(*) FROM pg_auth_members m JOIN pg_roles r ON r.oid = m.roleid JOIN pg_roles u ON u.oid = m.member WHERE u.rolname = 'clinic_backup' AND r.rolname = 'pg_read_all_data'"));
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>("""
            SELECT count(*) FROM information_schema.role_table_grants
            WHERE grantee = 'clinic_backup' AND privilege_type IN ('INSERT', 'UPDATE', 'DELETE', 'TRUNCATE', 'REFERENCES', 'TRIGGER')
            """));
        foreach (var sql in new[]
                 {
                     "SELECT count(*) FROM staff.assistants", "SELECT count(*) FROM staff.attendance_records", "SELECT count(*) FROM payroll.assistant_payrolls",
                     "SELECT count(*) FROM finance.patient_credit_entries", "SELECT count(*) FROM finance.doctor_entitlements",
                     "SELECT count(*) FROM orthodontics.ortho_installment_revisions", "SELECT count(*) FROM expenses.clinic_expenses",
                     "SELECT count(*) FROM identity.users", "SELECT count(*) FROM audit.audit_log", "SELECT count(*) FROM infra.\"__EFMigrationsHistory\"",
                 })
        {
            Assert.Null(await fixture.Database.TryExecuteAsync(sql, "clinic_backup"));
        }

        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("UPDATE configuration.settings SET value = value", "clinic_backup"));
        await using var connection = new NpgsqlConnection(fixture.Database.ConnectionFor("clinic_backup"));
        var manifest = await OperationalChecks.ManifestAsync(connection, CancellationToken.None);
        Assert.Contains(manifest, m => m.Key == "rows:finance.payments");
        Assert.DoesNotContain(manifest, m => m.Key.Contains("data_protection_keys", StringComparison.Ordinal));
    }

    [Fact] // §3.7 #6 / #16 / #18: logical restore drill from a clinic_backup dump into a throwaway database
    public async Task Backup_dump_restores_completely_into_a_throwaway_database()
    {
        var source = fixture.Database;
        var dumpFile = Path.Combine(Path.GetTempPath(), $"clinic-drill-{Guid.NewGuid():N}.dump");
        await using var target = await TestDatabase.CreateEmptyAsync();
        try
        {
            // Same flags as deploy/backup/backup.sh: custom format, the key table excluded (never locked or read), run as clinic_backup.
            await PgToolAsync("pg_dump", source.AdminConnection, source.Name,
                "--role=clinic_backup", "--format=custom", "--exclude-table=infra.data_protection_keys", "--file", dumpFile);
            Assert.True(new FileInfo(dumpFile).Length > 10_000);

            var listing = await PgToolAsync("pg_restore", source.AdminConnection, null, "--list", dumpFile);
            Assert.Contains("TABLE DATA finance payments", listing);
            Assert.DoesNotContain("data_protection_keys", listing);

            await PgToolAsync("pg_restore", target.AdminConnection, target.Name, "--role=clinic_migrator", "--exit-on-error", "--single-transaction", dumpFile);
            await using (var migrator = new NpgsqlConnection(target.ConnectionFor("clinic_migrator")))
            {
                await migrator.OpenAsync();
                await using var command = new NpgsqlCommand(OperationalChecks.Read(OperationalChecks.RestoreKeyTableFile), migrator);
                await command.ExecuteNonQueryAsync();
            }

            // The recreated key table matches the migrated one exactly (definition, identity, grants) and is empty.
            const string keyTable = """
                SELECT column_name || ':' || data_type || ':' || is_nullable || ':' || is_identity || ':' || COALESCE(identity_generation, '')
                FROM information_schema.columns WHERE table_schema = 'infra' AND table_name = 'data_protection_keys' ORDER BY column_name
                """;
            Assert.Equal(await source.ColumnAsync(keyTable), await target.ColumnAsync(keyTable));
            const string keyGrants = """
                SELECT grantee || ':' || privilege_type FROM information_schema.role_table_grants
                WHERE table_schema = 'infra' AND table_name = 'data_protection_keys' AND grantee <> 'clinic_migrator' ORDER BY 1
                """;
            Assert.Equal(await source.ColumnAsync(keyGrants), await target.ColumnAsync(keyGrants));

            await using var sourceConnection = new NpgsqlConnection(source.ConnectionFor("clinic_backup"));
            await using var targetConnection = new NpgsqlConnection(target.ConnectionFor("clinic_backup"));
            var before = await OperationalChecks.ManifestAsync(sourceConnection, CancellationToken.None);
            var after = await OperationalChecks.ManifestAsync(targetConnection, CancellationToken.None);
            Assert.Equal(before, after);
            Assert.All(await OperationalChecks.RunIntegrityAsync(targetConnection, CancellationToken.None), r => Assert.Equal(0, r.Violations));

            const string triggers = "SELECT count(*) FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid WHERE NOT t.tgisinternal";
            Assert.Equal(await source.ScalarAsync<long>(triggers), await target.ScalarAsync<long>(triggers));
            const string grants = "SELECT count(*) FROM information_schema.role_table_grants WHERE grantee IN ('clinic_app','clinic_worker','clinic_backup')";
            Assert.Equal(await source.ScalarAsync<long>(grants), await target.ScalarAsync<long>(grants));
            const string columnGrants = "SELECT count(*) FROM information_schema.column_privileges WHERE grantee IN ('clinic_app','clinic_worker')";
            Assert.Equal(await source.ScalarAsync<long>(columnGrants), await target.ScalarAsync<long>(columnGrants));
            Assert.Equal(0, await target.ScalarAsync<long>("SELECT count(*) FROM infra.data_protection_keys"));
            Assert.Equal(["clinic_migrator"], await target.ColumnAsync("""
                SELECT DISTINCT tableowner FROM pg_tables
                WHERE schemaname IN ('finance','clinical','identity','audit','payroll','expenses','orthodontics','patients','staff')
                """));

            // The restored schema is complete: the migrator is a no-op and a guard still fires.
            await target.MigrateAsync();
            Assert.Equal(await source.ScalarAsync<long>("SELECT count(*) FROM infra.\"__EFMigrationsHistory\""),
                await target.ScalarAsync<long>("SELECT count(*) FROM infra.\"__EFMigrationsHistory\""));
            Assert.Equal("42501", await target.TryExecuteAsync("DELETE FROM finance.payments", "clinic_app"));
        }
        finally
        {
            File.Delete(dumpFile);
        }
    }

    /// <summary>Runs a PostgreSQL client tool with the admin login; the password (if any) goes to the child environment only.</summary>
    internal static async Task<string> PgToolAsync(string tool, string adminConnection, string? database, params string[] args)
    {
        var builder = new NpgsqlConnectionStringBuilder(adminConnection);
        var bin = Environment.GetEnvironmentVariable("CLINIC_PG_BIN");
        var info = new ProcessStartInfo(string.IsNullOrWhiteSpace(bin) ? tool : Path.Combine(bin, tool))
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        info.ArgumentList.Add("--host=" + builder.Host);
        info.ArgumentList.Add("--port=" + builder.Port);
        info.ArgumentList.Add("--username=" + builder.Username);
        info.ArgumentList.Add("--no-password");
        if (database is not null)
        {
            info.ArgumentList.Add("--dbname=" + database);
        }

        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        if (!string.IsNullOrEmpty(builder.Password))
        {
            info.Environment["PGPASSWORD"] = builder.Password;
        }

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{tool} exited {process.ExitCode}: {await stderr}");
        }

        return await stdout;
    }
}
