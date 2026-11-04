using System;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Reports;

namespace ExpenseHub.Api.Contracts;

/// <summary>
/// Envelope de uma solicitação de relatório.
/// </summary>
/// <param name="Id">Identificador gerado pelo servidor.</param>
/// <param name="Scope">Escopo registrado.</param>
/// <param name="FromDate">Primeiro dia do período.</param>
/// <param name="ToDate">Último dia do período.</param>
/// <param name="Status">Estado do processamento.</param>
/// <param name="CreatedAt">Instante UTC do registro.</param>
/// <param name="StartedAt">Instante UTC do início do processamento.</param>
/// <param name="CompletedAt">Instante UTC da conclusão ou falha.</param>
/// <param name="Result">Resultado salvo, somente em Completed.</param>
/// <param name="Error">Erro sanitizado, somente em Failed ou quando a publicação não foi confirmada.</param>
public sealed record ReportResponse(
    Guid Id,
    ReportScope Scope,
    DateOnly FromDate,
    DateOnly ToDate,
    ReportStatus Status,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    ReportResult? Result,
    ReportErrorResponse? Error)
{
    /// <summary>
    /// Código usado quando a solicitação foi salva, mas o broker não confirmou a publicação.
    /// </summary>
    public const string PublishNotConfirmedCode = "PublishNotConfirmed";

    /// <summary>
    /// Converte a entidade no envelope, lendo o resultado salvo sem recalcular.
    /// </summary>
    /// <param name="report">Solicitação persistida.</param>
    /// <returns>O envelope.</returns>
    public static ReportResponse FromEntity(ReportRequest report)
    {
        ArgumentNullException.ThrowIfNull(report);

        ReportErrorResponse? error = report.ErrorCode is null
            ? null
            : new ReportErrorResponse(report.ErrorCode, report.ErrorMessage ?? string.Empty);

        return new ReportResponse(
            report.Id,
            report.Scope,
            report.FromDate,
            report.ToDate,
            report.Status,
            report.CreatedAtUtc,
            report.StartedAtUtc,
            report.CompletedAtUtc,
            report.GetResult(),
            error);
    }

    /// <summary>
    /// Envelope do 503: a solicitação existe com o id informado, mas a mensagem não foi confirmada pelo broker.
    /// </summary>
    /// <param name="report">Solicitação persistida.</param>
    /// <returns>O envelope com o erro sanitizado.</returns>
    public static ReportResponse PublishNotConfirmed(ReportRequest report)
    {
        return FromEntity(report) with
        {
            Error = new ReportErrorResponse(
                PublishNotConfirmedCode,
                "A solicitação foi registrada, mas a fila não confirmou o recebimento. Faça um novo pedido mais tarde."),
        };
    }
}

/// <summary>
/// Erro sanitizado de uma solicitação.
/// </summary>
/// <param name="Code">Código estável.</param>
/// <param name="Message">Mensagem sem detalhes internos.</param>
public sealed record ReportErrorResponse(string Code, string Message);
