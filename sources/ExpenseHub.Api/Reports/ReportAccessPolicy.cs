using System;
using ExpenseHub.Api.Auth;
using ExpenseHub.Api.Domain;

namespace ExpenseHub.Api.Reports;

/// <summary>
/// Matriz de acesso dos relatórios: Employee pede Mine, Auditor pede Global; consulta só do próprio solicitante.
/// </summary>
public static class ReportAccessPolicy
{
    /// <summary>
    /// Indica se o usuário pode solicitar o escopo informado.
    /// </summary>
    /// <param name="user">Usuário autenticado.</param>
    /// <param name="scope">Escopo pedido.</param>
    /// <returns><see langword="true"/> quando o perfil permite o escopo.</returns>
    public static bool CanRequest(CurrentUser user, ReportScope scope)
    {
        ArgumentNullException.ThrowIfNull(user);

        return scope switch
        {
            ReportScope.Mine => user.IsInRole(Roles.Employee),
            ReportScope.Global => user.IsInRole(Roles.Auditor),
            _ => false,
        };
    }

    /// <summary>
    /// Indica se o usuário pode ver a solicitação: somente quem a criou, inclusive para Auditor e Admin.
    /// </summary>
    /// <param name="user">Usuário autenticado.</param>
    /// <param name="report">Solicitação persistida.</param>
    /// <returns><see langword="true"/> quando o usuário é o solicitante.</returns>
    public static bool CanView(CurrentUser user, ReportRequest report)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(report);

        return string.Equals(report.RequesterId, user.Id, StringComparison.Ordinal);
    }
}
