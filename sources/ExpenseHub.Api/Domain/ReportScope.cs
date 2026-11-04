namespace ExpenseHub.Api.Domain;

/// <summary>
/// Escopo dos reembolsos considerados por um relatório.
/// </summary>
public enum ReportScope
{
    /// <summary>
    /// Somente os reembolsos do próprio solicitante. Exige a role Employee.
    /// </summary>
    Mine = 0,

    /// <summary>
    /// Reembolsos de todos os funcionários. Exige a role Auditor.
    /// </summary>
    Global = 1,
}
