using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;

namespace ExpenseHub.Api.Reports;

/// <summary>
/// Acesso a dados das solicitações de relatório e dos reembolsos usados no cálculo.
/// </summary>
public interface IReportRepository
{
    /// <summary>
    /// Adiciona uma nova solicitação ao contexto (gravada em <see cref="SaveChangesAsync"/>).
    /// </summary>
    /// <param name="report">Solicitação nova.</param>
    void Add(ReportRequest report);

    /// <summary>
    /// Busca uma solicitação rastreada pelo contexto.
    /// </summary>
    /// <param name="id">Identificador da solicitação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>A solicitação, ou <see langword="null"/> quando não existe.</returns>
    Task<ReportRequest?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Lista as solicitações criadas pelo solicitante, mais recentes primeiro.
    /// </summary>
    /// <param name="requesterId">Identificador do solicitante.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>As solicitações do solicitante.</returns>
    Task<IReadOnlyList<ReportRequest>> ListByRequesterAsync(string requesterId, CancellationToken cancellationToken);

    /// <summary>
    /// Lê estado e valor atuais dos reembolsos que atendem ao filtro.
    /// </summary>
    /// <param name="query">Filtro de período e escopo.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Os reembolsos considerados no cálculo.</returns>
    Task<IReadOnlyList<ReportExpenseRow>> ListExpensesAsync(ReportExpenseQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Grava as alterações pendentes.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Tarefa da gravação.</returns>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
