namespace ExpenseHub.Api.Domain;

/// <summary>
/// Estados de processamento de uma solicitação de relatório.
/// </summary>
public enum ReportStatus
{
    /// <summary>
    /// Registrada e aguardando o worker.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Em processamento pelo worker.
    /// </summary>
    Processing = 1,

    /// <summary>
    /// Concluída com resultado salvo. Estado final.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Falha definitiva registrada. Estado final.
    /// </summary>
    Failed = 3,
}
