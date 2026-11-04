using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Auth;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Dtos;
using ExpenseHub.Api.Errors;
using Microsoft.Extensions.Logging;

namespace ExpenseHub.Api.Reports;

/// <summary>
/// Implementação dos casos de uso dos relatórios na aplicação web.
/// </summary>
public sealed partial class ReportService : IReportService
{
    private readonly IReportRepository _repository;
    private readonly IReportPublisher _publisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReportService> _logger;

    /// <summary>
    /// Inicializa o serviço.
    /// </summary>
    /// <param name="repository">Repositório de relatórios.</param>
    /// <param name="publisher">Publicador da fila.</param>
    /// <param name="timeProvider">Fonte de tempo.</param>
    /// <param name="logger">Logger.</param>
    public ReportService(
        IReportRepository repository,
        IReportPublisher publisher,
        TimeProvider timeProvider,
        ILogger<ReportService> logger)
    {
        _repository = repository;
        _publisher = publisher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ReportSubmission> RequestAsync(
        CurrentUser user,
        CreateReportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);

        if (request.FromDate is not DateOnly fromDate || request.ToDate is not DateOnly toDate)
        {
            throw new DomainValidationException("fromDate e toDate são obrigatórios no formato yyyy-MM-dd.");
        }

        ReportScope scope = ParseScope(request.Scope);

        if (fromDate > toDate)
        {
            throw new DomainValidationException("fromDate deve ser menor ou igual a toDate.");
        }

        if (!ReportAccessPolicy.CanRequest(user, scope))
        {
            throw new ForbiddenOperationException($"O perfil do usuário não permite solicitar o escopo {scope}.");
        }

        var report = ReportRequest.Create(user.Id, scope, fromDate, toDate, _timeProvider.GetUtcNow().UtcDateTime);

        _repository.Add(report);
        await _repository.SaveChangesAsync(cancellationToken);
        LogPersisted(report.Id, report.Scope);

        try
        {
            await _publisher.PublishAsync(report.Id, cancellationToken);
        }
        catch (ReportPublishException exception)
        {
            LogPublishFailed(exception, report.Id);

            return new ReportSubmission(report, false);
        }

        LogPublished(report.Id);

        return new ReportSubmission(report, true);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReportRequest>> ListAsync(CurrentUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        return _repository.ListByRequesterAsync(user.Id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReportRequest> GetAsync(CurrentUser user, Guid id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        ReportRequest? report = await _repository.FindAsync(id, cancellationToken);

        if (report is null || !ReportAccessPolicy.CanView(user, report))
        {
            throw new ResourceNotFoundException("Solicitação de relatório não encontrada.");
        }

        return report;
    }

    /// <inheritdoc />
    public async Task<byte[]> ExportExcelAsync(CurrentUser user, Guid id, CancellationToken cancellationToken)
    {
        ReportRequest report = await GetAsync(user, id, cancellationToken);

        if (report.Status != ReportStatus.Completed)
        {
            throw new StateConflictException(
                $"O relatório está {report.Status}; a planilha só existe para solicitações Completed.");
        }

        return ReportExcelWriter.Write(report);
    }

    private static ReportScope ParseScope(string? scope)
    {
        return scope switch
        {
            nameof(ReportScope.Mine) => ReportScope.Mine,
            nameof(ReportScope.Global) => ReportScope.Global,
            _ => throw new DomainValidationException("O escopo deve ser Mine ou Global."),
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Relatório {ReportId} registrado como Pending (escopo {Scope}).")]
    private partial void LogPersisted(Guid reportId, ReportScope scope);

    [LoggerMessage(Level = LogLevel.Information, Message = "Relatório {ReportId} publicado com confirmação do broker.")]
    private partial void LogPublished(Guid reportId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Relatório {ReportId} persistido, mas a publicação não foi confirmada.")]
    private partial void LogPublishFailed(Exception exception, Guid reportId);
}
