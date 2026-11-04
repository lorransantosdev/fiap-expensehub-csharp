using System;
using System.Threading;
using System.Threading.Tasks;

namespace ExpenseHub.Api.Reports;

/// <summary>
/// Publica a mensagem de uma solicitação já persistida na fila de relatórios.
/// </summary>
public interface IReportPublisher
{
    /// <summary>
    /// Publica <c>{"reportId": ...}</c> e só retorna depois da confirmação do broker.
    /// </summary>
    /// <param name="reportId">Identificador da solicitação persistida.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Tarefa concluída quando o broker confirma a publicação.</returns>
    /// <exception cref="ReportPublishException">Quando a publicação não é confirmada.</exception>
    Task PublishAsync(Guid reportId, CancellationToken cancellationToken);
}
