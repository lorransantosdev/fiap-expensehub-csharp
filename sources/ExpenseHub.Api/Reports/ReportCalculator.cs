using System;
using System.Collections.Generic;
using System.Linq;
using ExpenseHub.Api.Domain;

namespace ExpenseHub.Api.Reports;

/// <summary>
/// Cálculo puro do resumo por estado, sem acesso a banco ou fila.
/// </summary>
public static class ReportCalculator
{
    /// <summary>
    /// Agrupa os reembolsos por estado e soma quantidades e valores, sempre devolvendo os cinco estados.
    /// </summary>
    /// <param name="rows">Reembolsos já filtrados por período e escopo.</param>
    /// <param name="generatedAtUtc">Instante UTC do cálculo.</param>
    /// <returns>O resultado do relatório.</returns>
    public static ReportResult Calculate(IEnumerable<ReportExpenseRow> rows, DateTime generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var counts = new Dictionary<ExpenseStatus, int>();
        var amounts = new Dictionary<ExpenseStatus, decimal>();

        foreach (ReportExpenseRow row in rows)
        {
            counts[row.Status] = counts.GetValueOrDefault(row.Status) + 1;
            amounts[row.Status] = amounts.GetValueOrDefault(row.Status) + row.Amount;
        }

        List<ReportStatusSummary> byStatus = Enum.GetValues<ExpenseStatus>()
            .Select(status => new ReportStatusSummary(
                status,
                counts.GetValueOrDefault(status),
                amounts.GetValueOrDefault(status)))
            .ToList();

        return new ReportResult(
            generatedAtUtc,
            byStatus.Sum(summary => summary.Count),
            byStatus.Sum(summary => summary.Amount),
            byStatus);
    }
}
