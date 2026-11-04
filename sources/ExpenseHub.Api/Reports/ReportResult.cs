using System;
using System.Collections.Generic;
using ExpenseHub.Api.Domain;

namespace ExpenseHub.Api.Reports;

/// <summary>
/// Resultado salvo de um relatório: totais gerais e por estado do reembolso.
/// </summary>
/// <param name="GeneratedAt">Instante UTC em que o cálculo foi feito.</param>
/// <param name="TotalCount">Quantidade total de reembolsos considerados.</param>
/// <param name="TotalAmount">Soma dos valores considerados.</param>
/// <param name="ByStatus">Quantidade e valor de cada um dos cinco estados, inclusive os zerados.</param>
public sealed record ReportResult(
    DateTime GeneratedAt,
    int TotalCount,
    decimal TotalAmount,
    IReadOnlyList<ReportStatusSummary> ByStatus);

/// <summary>
/// Quantidade e valor de um estado do reembolso dentro do relatório.
/// </summary>
/// <param name="Status">Estado do reembolso.</param>
/// <param name="Count">Quantidade de reembolsos no estado.</param>
/// <param name="Amount">Soma dos valores no estado.</param>
public sealed record ReportStatusSummary(ExpenseStatus Status, int Count, decimal Amount);
