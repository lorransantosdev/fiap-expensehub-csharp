using System;
using ExpenseHub.Api.Errors;
using ExpenseHub.Api.Reports;

namespace ExpenseHub.Api.Domain;

/// <summary>
/// Solicitação de relatório de reembolsos por período, processada em segundo plano pelo worker.
/// </summary>
public class ReportRequest
{
    /// <summary>
    /// Quantidade máxima de tentativas de processamento antes de registrar falha definitiva.
    /// </summary>
    public const int MaxAttempts = 5;

    /// <summary>
    /// Tamanho máximo do código de erro.
    /// </summary>
    public const int ErrorCodeMaxLength = 64;

    /// <summary>
    /// Tamanho máximo da mensagem de erro sanitizada.
    /// </summary>
    public const int ErrorMessageMaxLength = 300;

    /// <summary>
    /// Construtor usado pelo Entity Framework Core.
    /// </summary>
    private ReportRequest()
    {
    }

    /// <summary>
    /// Obtém o identificador gerado pelo servidor.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Obtém o identificador do solicitante, obtido do token autenticado.
    /// </summary>
    public string RequesterId { get; private set; } = string.Empty;

    /// <summary>
    /// Obtém o escopo registrado na solicitação.
    /// </summary>
    public ReportScope Scope { get; private set; }

    /// <summary>
    /// Obtém o primeiro dia do período, inclusive.
    /// </summary>
    public DateOnly FromDate { get; private set; }

    /// <summary>
    /// Obtém o último dia do período, inclusive.
    /// </summary>
    public DateOnly ToDate { get; private set; }

    /// <summary>
    /// Obtém o estado do processamento.
    /// </summary>
    public ReportStatus Status { get; private set; }

    /// <summary>
    /// Obtém o instante UTC do registro.
    /// </summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Obtém o instante UTC em que a última tentativa de processamento começou.
    /// </summary>
    public DateTime? StartedAtUtc { get; private set; }

    /// <summary>
    /// Obtém o instante UTC de conclusão ou de falha definitiva.
    /// </summary>
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>
    /// Obtém o resultado salvo em JSON, preenchido somente em <see cref="ReportStatus.Completed"/>.
    /// </summary>
    public string? ResultJson { get; private set; }

    /// <summary>
    /// Obtém o código estável do erro, preenchido somente em <see cref="ReportStatus.Failed"/>.
    /// </summary>
    public string? ErrorCode { get; private set; }

    /// <summary>
    /// Obtém a mensagem sanitizada do erro, preenchida somente em <see cref="ReportStatus.Failed"/>.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Obtém quantas tentativas de processamento já começaram.
    /// </summary>
    public int Attempts { get; private set; }

    /// <summary>
    /// Obtém o token de concorrência, renovado a cada gravação pelo contexto.
    /// </summary>
    public Guid Version { get; private set; }

    /// <summary>
    /// Obtém um valor que indica se a solicitação já chegou a um estado final.
    /// </summary>
    public bool IsFinished => Status is ReportStatus.Completed or ReportStatus.Failed;

    /// <summary>
    /// Registra uma nova solicitação pendente.
    /// </summary>
    /// <param name="requesterId">Solicitante, vindo do token.</param>
    /// <param name="scope">Escopo pedido.</param>
    /// <param name="fromDate">Primeiro dia do período.</param>
    /// <param name="toDate">Último dia do período.</param>
    /// <param name="nowUtc">Instante atual em UTC.</param>
    /// <returns>A solicitação em <see cref="ReportStatus.Pending"/>.</returns>
    /// <exception cref="DomainValidationException">Quando o período é invertido ou o escopo é inválido.</exception>
    public static ReportRequest Create(
        string requesterId,
        ReportScope scope,
        DateOnly fromDate,
        DateOnly toDate,
        DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requesterId);

        if (!Enum.IsDefined(scope))
        {
            throw new DomainValidationException("O escopo deve ser Mine ou Global.");
        }

        if (fromDate > toDate)
        {
            throw new DomainValidationException("fromDate deve ser menor ou igual a toDate.");
        }

        return new ReportRequest
        {
            Id = Guid.NewGuid(),
            RequesterId = requesterId,
            Scope = scope,
            FromDate = fromDate,
            ToDate = toDate,
            Status = ReportStatus.Pending,
            CreatedAtUtc = nowUtc,
        };
    }

    /// <summary>
    /// Marca o início de uma tentativa de processamento. Pending ou Processing podem ser retomados.
    /// </summary>
    /// <param name="nowUtc">Instante atual em UTC.</param>
    /// <exception cref="StateConflictException">Quando a solicitação já terminou.</exception>
    public void StartProcessing(DateTime nowUtc)
    {
        EnsureNotFinished();

        Status = ReportStatus.Processing;
        StartedAtUtc = nowUtc;
        Attempts++;
    }

    /// <summary>
    /// Salva o resultado e conclui a solicitação.
    /// </summary>
    /// <param name="result">Resultado calculado.</param>
    /// <param name="nowUtc">Instante atual em UTC.</param>
    /// <exception cref="StateConflictException">Quando a solicitação já terminou.</exception>
    public void Complete(ReportResult result, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(result);
        EnsureNotFinished();

        Status = ReportStatus.Completed;
        CompletedAtUtc = nowUtc;
        ResultJson = ReportResultSerializer.Serialize(result);
        ErrorCode = null;
        ErrorMessage = null;
    }

    /// <summary>
    /// Registra uma falha definitiva, com código estável e mensagem sanitizada.
    /// </summary>
    /// <param name="code">Código estável do erro.</param>
    /// <param name="message">Mensagem sem detalhes internos.</param>
    /// <param name="nowUtc">Instante atual em UTC.</param>
    /// <exception cref="StateConflictException">Quando a solicitação já terminou.</exception>
    public void Fail(string code, string message, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        EnsureNotFinished();

        Status = ReportStatus.Failed;
        CompletedAtUtc = nowUtc;
        ResultJson = null;
        ErrorCode = Truncate(code, ErrorCodeMaxLength);
        ErrorMessage = Truncate(message, ErrorMessageMaxLength);
    }

    /// <summary>
    /// Lê o resultado salvo, sem recalcular.
    /// </summary>
    /// <returns>O resultado, ou <see langword="null"/> quando a solicitação não está concluída.</returns>
    public ReportResult? GetResult()
    {
        return ResultJson is null ? null : ReportResultSerializer.Deserialize(ResultJson);
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length > maxLength ? text[..maxLength] : text;
    }

    private void EnsureNotFinished()
    {
        if (IsFinished)
        {
            throw new StateConflictException("A solicitação de relatório já foi finalizada.");
        }
    }
}
