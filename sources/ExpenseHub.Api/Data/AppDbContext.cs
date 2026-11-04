using System;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Data.Configurations;
using ExpenseHub.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ExpenseHub.Api.Data;

/// <summary>
/// Contexto do Entity Framework Core do ExpenseHub.
/// </summary>
public class AppDbContext : IdentityDbContext<IdentityUser>
{
    /// <summary>
    /// Inicializa o contexto com as opções configuradas na injeção de dependência.
    /// </summary>
    /// <param name="options">Opções do contexto.</param>
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Obtém os reembolsos.
    /// </summary>
    public DbSet<Expense> Expenses => Set<Expense>();

    /// <summary>
    /// Obtém as categorias de despesa.
    /// </summary>
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();

    /// <summary>
    /// Obtém as entradas de histórico dos reembolsos.
    /// </summary>
    public DbSet<ExpenseHistory> ExpenseHistories => Set<ExpenseHistory>();

    /// <summary>
    /// Obtém os registros de pagamento.
    /// </summary>
    public DbSet<PaymentRecord> PaymentRecords => Set<PaymentRecord>();

    /// <summary>
    /// Obtém as solicitações de relatório.
    /// </summary>
    public DbSet<ReportRequest> ReportRequests => Set<ReportRequest>();

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RenewConcurrencyTokens();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RenewConcurrencyTokens();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    private void RenewConcurrencyTokens()
    {
        foreach (EntityEntry<Expense> entry in ChangeTracker.Entries<Expense>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Property(expense => expense.Version).CurrentValue = Guid.NewGuid();
            }
        }

        foreach (EntityEntry<ReportRequest> entry in ChangeTracker.Entries<ReportRequest>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Property(report => report.Version).CurrentValue = Guid.NewGuid();
            }
        }
    }
}
