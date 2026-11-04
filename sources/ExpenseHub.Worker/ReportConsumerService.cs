using System;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Messaging;
using ExpenseHub.Api.Reports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ExpenseHub.Worker;

/// <summary>
/// Consome a fila de relatórios com confirmação manual: o ack só é enviado depois que o efeito está salvo.
/// Falhas transitórias devolvem a mensagem à fila após uma espera; mensagens inválidas são rejeitadas sem reentrega.
/// </summary>
public sealed partial class ReportConsumerService : BackgroundService
{
    private static readonly TimeSpan _connectRetryDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<ReportConsumerService> _logger;

    /// <summary>
    /// Inicializa o consumidor.
    /// </summary>
    /// <param name="scopeFactory">Fábrica de escopos para um DbContext por mensagem.</param>
    /// <param name="options">Configuração do RabbitMQ.</param>
    /// <param name="logger">Logger.</param>
    public ReportConsumerService(
        IServiceScopeFactory scopeFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<ReportConsumerService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IConnection connection = await ConnectAsync(stoppingToken);
        await using (connection)
        {
            await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
            await _options.DeclareQueueAsync(channel, stoppingToken);
            await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, delivery) => HandleAsync(channel, delivery, stoppingToken);

            await channel.BasicConsumeAsync(_options.QueueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
            LogConsuming(_options.QueueName);

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                LogStopping();
            }
        }
    }

    private async Task<IConnection> ConnectAsync(CancellationToken stoppingToken)
    {
        ConnectionFactory factory = _options.CreateFactory("expensehub-worker");

        while (true)
        {
            try
            {
                return await factory.CreateConnectionAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogConnectRetry(exception.GetType().Name, _connectRetryDelay.TotalSeconds);
                await Task.Delay(_connectRetryDelay, stoppingToken);
            }
        }
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        if (!ReportMessage.TryParse(delivery.Body.Span, out ReportMessage? message) || message is null)
        {
            LogInvalidMessage(delivery.DeliveryTag);
            await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, cancellationToken: stoppingToken);

            return;
        }

        using IDisposable? logScope = _logger.BeginScope("ReportId:{ReportId}", message.ReportId);
        LogReceived(message.ReportId, delivery.Redelivered);

        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            ReportProcessor processor = scope.ServiceProvider.GetRequiredService<ReportProcessor>();

            ReportProcessingOutcome outcome = await processor.ProcessAsync(message.ReportId, stoppingToken);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            LogAcked(message.ReportId, outcome);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            // Nada foi confirmado: a persistência falhou ou não terminou. Espera e devolve para nova tentativa;
            // o limite de tentativas salvo no registro impede reencaminhamento infinito.
            LogTransientFailure(exception, message.ReportId, _options.RetryDelaySeconds);
            await Task.Delay(TimeSpan.FromSeconds(_options.RetryDelaySeconds), stoppingToken);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken: stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker consumindo a fila {QueueName} (prefetch 1, ack manual).")]
    private partial void LogConsuming(string queueName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker parando; mensagens sem ack voltam para a fila.")]
    private partial void LogStopping();

    [LoggerMessage(Level = LogLevel.Warning, Message = "RabbitMQ indisponível ({ErrorType}); nova tentativa em {DelaySeconds}s.")]
    private partial void LogConnectRetry(string errorType, double delaySeconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mensagem {DeliveryTag} sem reportId válido; rejeitada sem reentrega.")]
    private partial void LogInvalidMessage(ulong deliveryTag);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mensagem do relatório {ReportId} recebida (redelivered={Redelivered}).")]
    private partial void LogReceived(Guid reportId, bool redelivered);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mensagem do relatório {ReportId} confirmada (ack) com desfecho {Outcome}.")]
    private partial void LogAcked(Guid reportId, ReportProcessingOutcome outcome);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha transitória no relatório {ReportId}; sem ack, volta à fila em {DelaySeconds}s.")]
    private partial void LogTransientFailure(Exception exception, Guid reportId, int delaySeconds);
}
