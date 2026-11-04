using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;
using Microsoft.Extensions.Logging;

namespace ExpenseHub.Api.Reports;

/// <summary>
/// Resultado do tratamento de uma mensagem pelo worker. Em todos os casos a mensagem pode ser confirmada.
/// </summary>
public enum ReportProcessingOutcome
{
    /// <summary>
    /// Resultado calculado e salvo nesta execução.
    /// </summary>
    Completed = 0,

    /// <summary>
    /// A solicitação já estava finalizada: nenhum novo efeito (duplicata).
    /// </summary>
    Duplicate = 1,

    /// <summary>
    /// Falha definitiva registrada como <see cref="ReportStatus.Failed"/>.
    /// </summary>
    Failed = 2,

    /// <summary>
    /// A solicitação não existe no banco: nada a fazer.
    /// </summary>
    NotFound = 3,
}

/// <summary>
/// Processa uma solicitação: carrega o registro persistido, calcula e salva o resultado de forma idempotente.
/// Exceções transitórias (banco indisponível, concorrência) são propagadas para a mensagem voltar à fila.
/// </summary>
public sealed partial class ReportProcessor
{
    private readonly IReportRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReportProcessor> _logger;

    /// <summary>
    /// Inicializa o processador.
    /// </summary>
    /// <param name="repository">Repositório de relatórios.</param>
    /// <param name="timeProvider">Fonte de tempo.</param>
    /// <param name="logger">Logger.</param>
    public ReportProcessor(IReportRepository repository, TimeProvider timeProvider, ILogger<ReportProcessor> logger)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Processa a solicitação indicada pela mensagem.
    /// </summary>
    /// <param name="reportId">Identificador recebido na mensagem.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>O desfecho, sempre já persistido quando há efeito.</returns>
    public async Task<ReportProcessingOutcome> ProcessAsync(Guid reportId, CancellationToken cancellationToken)
    {
        ReportRequest? report = await _repository.FindAsync(reportId, cancellationToken);

        if (report is null)
        {
            LogNotFound(reportId);

            return ReportProcessingOutcome.NotFound;
        }

        if (report.IsFinished)
        {
            LogDuplicate(reportId, report.Status);

            return ReportProcessingOutcome.Duplicate;
        }

        if (report.Attempts >= ReportRequest.MaxAttempts)
        {
            report.Fail(
                "MaxAttemptsExceeded",
                "O relatório não pôde ser processado após várias tentativas.",
                UtcNow());
            await _repository.SaveChangesAsync(cancellationToken);
            LogFailed(reportId, "MaxAttemptsExceeded");

            return ReportProcessingOutcome.Failed;
        }

        report.StartProcessing(UtcNow());
        await _repository.SaveChangesAsync(cancellationToken);
        LogStarted(reportId, report.Attempts, report.Scope);

        IReadOnlyList<ReportExpenseRow> rows =
            await _repository.ListExpensesAsync(ReportExpenseQuery.For(report), cancellationToken);
        ReportResult result = ReportCalculator.Calculate(rows, UtcNow());

        report.Complete(result, UtcNow());
        await _repository.SaveChangesAsync(cancellationToken);
        LogCompleted(reportId, result.TotalCount);

        return ReportProcessingOutcome.Completed;
    }

    private DateTime UtcNow()
    {
        return _timeProvider.GetUtcNow().UtcDateTime;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Relatório {ReportId} não encontrado; mensagem descartada.")]
    private partial void LogNotFound(Guid reportId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Relatório {ReportId} já está {Status}; duplicata ignorada sem novo efeito.")]
    private partial void LogDuplicate(Guid reportId, ReportStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Relatório {ReportId} iniciado (tentativa {Attempt}, escopo {Scope}).")]
    private partial void LogStarted(Guid reportId, int attempt, ReportScope scope);

    [LoggerMessage(Level = LogLevel.Information, Message = "Relatório {ReportId} concluído com {TotalCount} reembolsos.")]
    private partial void LogCompleted(Guid reportId, int totalCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Relatório {ReportId} falhou definitivamente ({ErrorCode}).")]
    private partial void LogFailed(Guid reportId, string errorCode);
}
