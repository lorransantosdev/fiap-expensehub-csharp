using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Reports;
using Microsoft.EntityFrameworkCore;

namespace ExpenseHub.Api.Data;

/// <summary>
/// Implementação Entity Framework Core de <see cref="IReportRepository"/>.
/// </summary>
public sealed class ReportRepository : IReportRepository
{
    private readonly AppDbContext _dbContext;

    /// <summary>
    /// Inicializa o repositório.
    /// </summary>
    /// <param name="dbContext">Contexto do banco.</param>
    public ReportRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public void Add(ReportRequest report)
    {
        _dbContext.ReportRequests.Add(report);
    }

    /// <inheritdoc />
    public Task<ReportRequest?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        return _dbContext.ReportRequests.FirstOrDefaultAsync(report => report.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReportRequest>> ListByRequesterAsync(
        string requesterId,
        CancellationToken cancellationToken)
    {
        List<ReportRequest> reports = await _dbContext.ReportRequests
            .AsNoTracking()
            .Where(report => report.RequesterId == requesterId)
            .ToListAsync(cancellationToken);

        return reports.OrderByDescending(report => report.CreatedAtUtc).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReportExpenseRow>> ListExpensesAsync(
        ReportExpenseQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await _dbContext.Expenses
            .AsNoTracking()
            .Where(query.ToExpression())
            .Select(expense => new ReportExpenseRow(expense.Status, expense.Amount))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
