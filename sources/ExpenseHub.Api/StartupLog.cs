using Microsoft.Extensions.Logging;

namespace ExpenseHub.Api;

/// <summary>
/// Log de inicialização com a versão (SHA do commit) em execução, usado pela web e pelo worker.
/// </summary>
public static partial class StartupLog
{
    /// <summary>
    /// Registra o componente e a versão em execução.
    /// </summary>
    /// <param name="logger">Logger.</param>
    /// <param name="component">Nome do componente (web ou worker).</param>
    /// <param name="version">SHA do commit da imagem.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "ExpenseHub {Component} iniciado na versão {Version}.")]
    public static partial void Started(ILogger logger, string component, string version);
}
