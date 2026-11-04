using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Auth;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Dtos;

namespace ExpenseHub.Api.Reports;

/// <summary>
/// Casos de uso dos relatórios na aplicação web.
/// </summary>
public interface IReportService
{
    /// <summary>
    /// Valida, persiste e publica uma solicitação.
    /// </summary>
    /// <param name="user">Usuário autenticado.</param>
    /// <param name="request">Período e escopo.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>A solicitação persistida e se a publicação foi confirmada.</returns>
    Task<ReportSubmission> RequestAsync(CurrentUser user, CreateReportRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Lista somente as solicitações criadas pelo usuário.
    /// </summary>
    /// <param name="user">Usuário autenticado.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>As solicitações do usuário.</returns>
    Task<IReadOnlyList<ReportRequest>> ListAsync(CurrentUser user, CancellationToken cancellationToken);

    /// <summary>
    /// Consulta uma solicitação própria.
    /// </summary>
    /// <param name="user">Usuário autenticado.</param>
    /// <param name="id">Identificador da solicitação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>A solicitação.</returns>
    Task<ReportRequest> GetAsync(CurrentUser user, Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Monta a planilha a partir do resultado salvo de uma solicitação própria concluída.
    /// </summary>
    /// <param name="user">Usuário autenticado.</param>
    /// <param name="id">Identificador da solicitação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>O conteúdo do arquivo .xlsx.</returns>
    Task<byte[]> ExportExcelAsync(CurrentUser user, Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// Resultado do registro de uma solicitação.
/// </summary>
/// <param name="Report">Solicitação persistida.</param>
/// <param name="Published">Indica se o broker confirmou a publicação.</param>
public sealed record ReportSubmission(ReportRequest Report, bool Published);
