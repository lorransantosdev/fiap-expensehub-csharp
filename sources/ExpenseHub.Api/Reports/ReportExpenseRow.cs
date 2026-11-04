using ExpenseHub.Api.Domain;

namespace ExpenseHub.Api.Reports;

/// <summary>
/// Dados mínimos de um reembolso lidos pelo worker para o cálculo.
/// </summary>
/// <param name="Status">Estado lido durante o processamento.</param>
/// <param name="Amount">Valor lido durante o processamento.</param>
public sealed record ReportExpenseRow(ExpenseStatus Status, decimal Amount);
