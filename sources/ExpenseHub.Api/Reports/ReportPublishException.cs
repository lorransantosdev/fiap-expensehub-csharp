using System;

namespace ExpenseHub.Api.Reports;

/// <summary>
/// A publicação da mensagem não foi confirmada pelo broker.
/// </summary>
public sealed class ReportPublishException : Exception
{
    /// <summary>
    /// Inicializa a exceção com a mensagem padrão.
    /// </summary>
    public ReportPublishException()
        : base("A publicação da solicitação não foi confirmada pelo broker.")
    {
    }

    /// <summary>
    /// Inicializa a exceção com uma mensagem.
    /// </summary>
    /// <param name="message">Mensagem de erro.</param>
    public ReportPublishException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Inicializa a exceção com uma mensagem e a exceção interna.
    /// </summary>
    /// <param name="message">Mensagem de erro.</param>
    /// <param name="innerException">Exceção que causou o erro.</param>
    public ReportPublishException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
