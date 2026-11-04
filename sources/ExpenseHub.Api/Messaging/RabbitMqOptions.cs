using System;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;

namespace ExpenseHub.Api.Messaging;

/// <summary>
/// Configuração do RabbitMQ, lida da seção <c>RabbitMq</c> (a senha vem de variável de ambiente).
/// </summary>
public sealed class RabbitMqOptions
{
    /// <summary>
    /// Nome da seção de configuração.
    /// </summary>
    public const string SectionName = "RabbitMq";

    /// <summary>
    /// Obtém ou define o host do broker.
    /// </summary>
    public string HostName { get; set; } = "localhost";

    /// <summary>
    /// Obtém ou define a porta AMQP.
    /// </summary>
    public int Port { get; set; } = 5672;

    /// <summary>
    /// Obtém ou define o virtual host.
    /// </summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>
    /// Obtém ou define o usuário.
    /// </summary>
    public string UserName { get; set; } = "guest";

    /// <summary>
    /// Obtém ou define a senha.
    /// </summary>
    public string Password { get; set; } = "guest";

    /// <summary>
    /// Obtém ou define o nome da fila durável de relatórios.
    /// </summary>
    public string QueueName { get; set; } = "expensehub.reports";

    /// <summary>
    /// Obtém ou define o tempo máximo, em segundos, para o broker confirmar uma publicação.
    /// </summary>
    public int PublishTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Obtém ou define a espera, em segundos, antes de devolver à fila uma mensagem com falha transitória.
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 5;

    /// <summary>
    /// Cria a fábrica de conexões com recuperação automática.
    /// </summary>
    /// <param name="clientName">Nome exibido no painel do RabbitMQ.</param>
    /// <returns>A fábrica de conexões.</returns>
    public ConnectionFactory CreateFactory(string clientName)
    {
        return new ConnectionFactory
        {
            HostName = HostName,
            Port = Port,
            VirtualHost = VirtualHost,
            UserName = UserName,
            Password = Password,
            ClientProvidedName = clientName,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
        };
    }

    /// <summary>
    /// Declara a fila durável, sem exclusão automática, com os mesmos argumentos na web e no worker.
    /// </summary>
    /// <param name="channel">Canal aberto.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Tarefa da declaração.</returns>
    public Task DeclareQueueAsync(IChannel channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        return channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);
    }
}
