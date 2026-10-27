using Clinic.Modules.Configuration.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Clinic.SharedKernel.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Modules.Expenses;

/// <summary>
/// expenses.expense_categories (M, A): the 11 confirmed categories (seeded). Only Materials / Medical Supplies and Petty /
/// Miscellaneous are Sales-recordable. Categories are expense classes, never Inventory (FR-EXP-001).
/// </summary>
internal sealed class ExpenseCategory : IAuditable, IRowVersioned
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public bool IsActive { get; set; }
    public bool SalesCanRecord { get; set; }
    public int SortOrder { get; init; }
    public uint RowVersion { get; private set; }
}

/// <summary>
/// expenses.clinic_expenses (M, A+): no Finalized state, no adjustment rows, no attachment (FR-EXP-006); the Manager edits in
/// place (audited) and the creator / creator role / created_at are never overwritten (FR-EXP-005).
/// </summary>
internal sealed class ClinicExpense : IAuditable, IRowVersioned
{
    public Guid Id { get; init; }
    public DateOnly ExpenseDate { get; set; }
    public Guid CategoryId { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public string? PaymentMethodCode { get; set; }
    public string? PaidToVendor { get; set; }
    public string? Notes { get; set; }
    public Guid CreatedByUserId { get; init; }
    public required string CreatedByRole { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public uint RowVersion { get; private set; }
}

public sealed record AllowedCategoryDto(Guid Id, string Name);

public sealed record ExpenseCategoryDto(Guid Id, string Name, bool IsActive, bool SalesCanRecord, string Version);

public sealed record SalesExpenseRequest(Guid? CategoryId, decimal Amount, string Specification);

/// <summary>API A3: the Sales response echoes only the record just created.</summary>
public sealed record ExpenseCreatedDto(Guid Id, string Category, decimal Amount, DateOnly ExpenseDate);

public sealed record ExpenseRequest(DateOnly? ExpenseDate, Guid? CategoryId, string Description, decimal Amount, string? PaymentMethodCode, string? PaidToVendor,
    string? Notes);

public sealed record CategoryRequest(string Name, bool IsActive, bool SalesCanRecord);

public sealed record ExpenseDto(
    Guid Id, DateOnly ExpenseDate, Guid CategoryId, string Category, string Description, decimal Amount, string? PaymentMethodCode, string? PaidToVendor,
    string? Notes, Guid CreatedByUserId, string CreatedByRole, DateTimeOffset CreatedAt, Guid? UpdatedByUserId, DateTimeOffset? UpdatedAt, string Version);

internal sealed class SalesExpenseRequestValidator : AbstractValidator<SalesExpenseRequest>
{
    public SalesExpenseRequestValidator()
    {
        RuleFor(x => x.CategoryId).NotNull();
        RuleFor(x => x.Amount).GreaterThan(0).WithErrorCode(FieldErrorCodes.OutOfRange).TwoDecimals();
        RuleFor(x => x.Specification).NotEmpty().MaximumLength(1000);
    }
}

internal sealed class ExpenseRequestValidator : AbstractValidator<ExpenseRequest>
{
    public ExpenseRequestValidator()
    {
        RuleFor(x => x.ExpenseDate).NotNull();
        RuleFor(x => x.CategoryId).NotNull();
        RuleFor(x => x.Description).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Amount).GreaterThan(0).WithErrorCode(FieldErrorCodes.OutOfRange).TwoDecimals();
        RuleFor(x => x.PaymentMethodCode).Must(m => m is null || PaymentMethodCodes.All.Contains(m)).WithErrorCode(FieldErrorCodes.Invalid);
        RuleFor(x => x.PaidToVendor).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

internal sealed class CategoryRequestValidator : AbstractValidator<CategoryRequest>
{
    public CategoryRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
}

public static class ExpensesModule
{
    public const string Schema = "expenses";

    public static IServiceCollection AddExpensesModule(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<SalesExpenseRequestValidator>(includeInternalTypes: true);
        return services;
    }

    public static void ConfigureModel(ModelBuilder model)
    {
        model.Entity<ExpenseCategory>(e =>
        {
            e.ToTable("expense_categories", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            e.HasIndex(x => x.Name).IsUnique();
        });

        model.Entity<ClinicExpense>(e =>
        {
            e.ToTable("clinic_expenses", Schema, t =>
            {
                t.HasCheckConstraint("ck_clinic_expenses_amount", "amount > 0");
                t.HasCheckConstraint("ck_clinic_expenses_role", "created_by_role IN ('MANAGER', 'SALES_RECEPTION')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.PaymentMethodCode).HasMaxLength(20);
            e.Property(x => x.PaidToVendor).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.CreatedByRole).HasMaxLength(20);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            e.HasOne<ExpenseCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.ExpenseDate);
            e.HasIndex(x => new { x.CategoryId, x.ExpenseDate });
        });
    }

    /// <summary>FR-EXP-002 / -003 (Sales and Manager): allowed-category lookup and create-only operational expense.</summary>
    public static void MapSalesEndpoints(RouteGroupBuilder sales)
    {
        sales.MapGet("/expenses/allowed-categories", async (DbContext db, CancellationToken ct) =>
                Results.Ok(await db.Set<ExpenseCategory>().AsNoTracking().Where(c => c.IsActive && c.SalesCanRecord).OrderBy(c => c.SortOrder)
                    .Select(c => new AllowedCategoryDto(c.Id, c.Name)).ToListAsync(ct)))
            .RequirePermission(PermissionKeys.ExpensesCategoriesReadAllowed).Produces<List<AllowedCategoryDto>>();

        sales.MapPost("/expenses", async (SalesExpenseRequest body, DbContext db, ICurrentActor actor, IClinicSettings settings, IAuditWriter audit, IClock clock,
            CancellationToken ct) =>
        {
            var category = await db.Set<ExpenseCategory>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == body.CategoryId && c.IsActive && c.SalesCanRecord, ct)
                           ?? throw AppException.Rule("categoryId", FieldErrorCodes.NotAllowed);
            var now = clock.UtcNow;
            var expense = new ClinicExpense
            {
                Id = Guid.CreateVersion7(), ExpenseDate = await settings.GetClinicTodayAsync(now, ct), CategoryId = category.Id, Description = body.Specification.Trim(),
                Amount = body.Amount, CreatedByUserId = actor.UserId!.Value, CreatedByRole = actor.Role ?? Roles.SalesReception, CreatedAt = now,
            };
            db.Set<ClinicExpense>().Add(expense);
            audit.Add(new AuditEvent("expenses.expense.created", "expenses.clinic_expenses", expense.Id.ToString(),
                NewValue: new { category = category.Name, expense.Amount, expense.CreatedByRole }));
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/sales/expenses/{expense.Id}", new ExpenseCreatedDto(expense.Id, category.Name, expense.Amount, expense.ExpenseDate));
        }).RequirePermission(PermissionKeys.ExpensesCreateOperational).Produces<ExpenseCreatedDto>(StatusCodes.Status201Created).Validate<SalesExpenseRequest>();
    }

    /// <summary>FR-EXP-001 / -004 / -005 (Manager only). No adjustment, finalize, attachment or delete route exists (FR-EXP-006).</summary>
    public static void MapManagerEndpoints(RouteGroupBuilder manager)
    {
        manager.MapGet("/expenses", async (DateOnly? from, DateOnly? to, Guid? categoryId, DbContext db, CancellationToken ct) =>
        {
            var query = db.Set<ClinicExpense>().AsNoTracking();
            if (from is not null)
            {
                query = query.Where(e => e.ExpenseDate >= from);
            }

            if (to is not null)
            {
                query = query.Where(e => e.ExpenseDate <= to);
            }

            if (categoryId is not null)
            {
                query = query.Where(e => e.CategoryId == categoryId);
            }

            var rows = await query.OrderByDescending(e => e.ExpenseDate).ThenByDescending(e => e.CreatedAt).Take(2000).ToListAsync(ct);
            return Results.Ok(await ProjectAsync(db, rows, ct));
        }).RequirePermission(PermissionKeys.ExpensesReadAll).Produces<List<ExpenseDto>>();

        manager.MapPost("/expenses", async (ExpenseRequest body, DbContext db, ICurrentActor actor, IAuditWriter audit, IClock clock, CancellationToken ct) =>
        {
            var category = await RequireCategoryAsync(db, body.CategoryId!.Value, ct);
            var expense = new ClinicExpense
            {
                Id = Guid.CreateVersion7(), ExpenseDate = body.ExpenseDate!.Value, CategoryId = category.Id, Description = body.Description.Trim(), Amount = body.Amount,
                PaymentMethodCode = body.PaymentMethodCode, PaidToVendor = Clean(body.PaidToVendor), Notes = Clean(body.Notes), CreatedByUserId = actor.UserId!.Value,
                CreatedByRole = Roles.Manager, CreatedAt = clock.UtcNow,
            };
            db.Set<ClinicExpense>().Add(expense);
            audit.Add(new AuditEvent("expenses.expense.created", "expenses.clinic_expenses", expense.Id.ToString(),
                NewValue: new { category = category.Name, expense.Amount, expense.ExpenseDate }));
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/manager/expenses/{expense.Id}", (await ProjectAsync(db, [expense], ct))[0]);
        }).RequirePermission(PermissionKeys.ExpensesManage).Produces<ExpenseDto>(StatusCodes.Status201Created).Validate<ExpenseRequest>();

        manager.MapPut("/expenses/{id:guid}", async (Guid id, ExpenseRequest body, HttpRequest request, DbContext db, ICurrentActor actor, IAuditWriter audit,
            IClock clock, CancellationToken ct) =>
        {
            var expense = await db.Set<ClinicExpense>().SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw AppException.NotFound();
            ETags.Require(request, expense.RowVersion);
            var category = await RequireCategoryAsync(db, body.CategoryId!.Value, ct);
            var previous = new { expense.ExpenseDate, expense.CategoryId, expense.Description, expense.Amount, expense.PaymentMethodCode, expense.PaidToVendor, expense.Notes };
            expense.ExpenseDate = body.ExpenseDate!.Value;
            expense.CategoryId = category.Id;
            expense.Description = body.Description.Trim();
            expense.Amount = body.Amount;
            expense.PaymentMethodCode = body.PaymentMethodCode;
            expense.PaidToVendor = Clean(body.PaidToVendor);
            expense.Notes = Clean(body.Notes);
            expense.UpdatedByUserId = actor.UserId;
            expense.UpdatedAt = clock.UtcNow;
            audit.Add(new AuditEvent("expenses.expense.updated", "expenses.clinic_expenses", id.ToString(), PreviousValue: previous,
                NewValue: new { expense.ExpenseDate, expense.CategoryId, expense.Description, expense.Amount, expense.PaymentMethodCode, expense.PaidToVendor, expense.Notes }));
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw AppException.Stale();
            }

            return Results.Ok((await ProjectAsync(db, [expense], ct))[0]);
        }).RequirePermission(PermissionKeys.ExpensesManage).Produces<ExpenseDto>().Validate<ExpenseRequest>();

        manager.MapGet("/expense-categories", async (DbContext db, CancellationToken ct) =>
                Results.Ok((await db.Set<ExpenseCategory>().AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync(ct))
                    .Select(c => new ExpenseCategoryDto(c.Id, c.Name, c.IsActive, c.SalesCanRecord, ETags.Format(c.RowVersion))).ToList()))
            .RequirePermission(PermissionKeys.ExpenseCategoriesManage).Produces<List<ExpenseCategoryDto>>();

        manager.MapPut("/expense-categories/{id:guid}", async (Guid id, CategoryRequest body, HttpRequest request, DbContext db, IAuditWriter audit,
            CancellationToken ct) =>
        {
            var category = await db.Set<ExpenseCategory>().SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw AppException.NotFound();
            ETags.Require(request, category.RowVersion);
            var previous = new { category.Name, category.IsActive, category.SalesCanRecord };
            category.Name = body.Name.Trim();
            category.IsActive = body.IsActive;
            category.SalesCanRecord = body.SalesCanRecord;
            audit.Add(new AuditEvent("expenses.category.updated", "expenses.expense_categories", id.ToString(), PreviousValue: previous,
                NewValue: new { category.Name, category.IsActive, category.SalesCanRecord }));
            await db.SaveChangesAsync(ct);
            return Results.Ok(new ExpenseCategoryDto(category.Id, category.Name, category.IsActive, category.SalesCanRecord, ETags.Format(category.RowVersion)));
        }).RequirePermission(PermissionKeys.ExpenseCategoriesManage).Produces<ExpenseCategoryDto>().Validate<CategoryRequest>();
    }

    private static async Task<ExpenseCategory> RequireCategoryAsync(DbContext db, Guid id, CancellationToken ct) =>
        await db.Set<ExpenseCategory>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && c.IsActive, ct)
        ?? throw AppException.Rule("categoryId", FieldErrorCodes.Invalid);

    private static async Task<List<ExpenseDto>> ProjectAsync(DbContext db, IReadOnlyList<ClinicExpense> rows, CancellationToken ct)
    {
        var categories = await db.Set<ExpenseCategory>().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return rows.Select(e => new ExpenseDto(e.Id, e.ExpenseDate, e.CategoryId, categories.GetValueOrDefault(e.CategoryId) ?? "", e.Description, e.Amount,
            e.PaymentMethodCode, e.PaidToVendor, e.Notes, e.CreatedByUserId, e.CreatedByRole, e.CreatedAt, e.UpdatedByUserId, e.UpdatedAt,
            ETags.Format(e.RowVersion))).ToList();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
