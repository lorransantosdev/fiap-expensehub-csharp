using System;
using System.Linq.Expressions;
using ExpenseHub.Api.Domain;

namespace ExpenseHub.Api.Reports;

/// <summary>
/// Filtro dos reembolsos de um relatório, montado somente a partir do registro persistido.
/// </summary>
/// <param name="OwnerId">Proprietário exigido (escopo Mine), ou <see langword="null"/> no escopo Global.</param>
/// <param name="FromDate">Primeiro dia do período, inclusive.</param>
/// <param name="ToDate">Último dia do período, inclusive.</param>
public sealed record ReportExpenseQuery(string? OwnerId, DateOnly FromDate, DateOnly ToDate)
{
    /// <summary>
    /// Monta o filtro da solicitação: no escopo Mine, o proprietário é sempre o solicitante registrado no servidor.
    /// </summary>
    /// <param name="report">Solicitação persistida.</param>
    /// <returns>O filtro.</returns>
    public static ReportExpenseQuery For(ReportRequest report)
    {
        ArgumentNullException.ThrowIfNull(report);

        string? ownerId = report.Scope == ReportScope.Mine ? report.RequesterId : null;

        return new ReportExpenseQuery(ownerId, report.FromDate, report.ToDate);
    }

    /// <summary>
    /// Converte o filtro em expressão traduzível pelo Entity Framework, usando a data da despesa (inclusive nas bordas).
    /// </summary>
    /// <returns>A expressão do filtro.</returns>
    public Expression<Func<Expense, bool>> ToExpression()
    {
        string? ownerId = OwnerId;
        DateOnly fromDate = FromDate;
        DateOnly toDate = ToDate;

        if (ownerId is null)
        {
            return expense => expense.ExpenseDate >= fromDate && expense.ExpenseDate <= toDate;
        }

        return expense => expense.OwnerId == ownerId
            && expense.ExpenseDate >= fromDate
            && expense.ExpenseDate <= toDate;
    }
}
