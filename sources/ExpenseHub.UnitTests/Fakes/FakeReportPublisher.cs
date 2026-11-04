using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Reports;

namespace ExpenseHub.UnitTests.Fakes;

/// <summary>
/// Publicador falso: registra os ids publicados ou simula a falta de confirmação do broker.
/// </summary>
public sealed class FakeReportPublisher : IReportPublisher
{
    /// <summary>
    /// Obtém os ids publicados com confirmação.
    /// </summary>
    public List<Guid> Published { get; } = [];

    /// <summary>
    /// Obtém ou define um valor que indica se o broker deve deixar de confirmar.
    /// </summary>
    public bool FailConfirmation { get; set; }

    /// <inheritdoc />
    public Task PublishAsync(Guid reportId, CancellationToken cancellationToken)
    {
        if (FailConfirmation)
        {
            throw new ReportPublishException("Broker não confirmou (simulado).");
        }

        Published.Add(reportId);

        return Task.CompletedTask;
    }
}
