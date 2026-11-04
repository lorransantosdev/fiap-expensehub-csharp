using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Reports;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace ExpenseHub.Api.Messaging;

/// <summary>
/// Publicador com confirmação do broker (publisher confirms), mensagem persistente e fila durável.
/// Mantém uma conexão e um canal reaproveitados; qualquer falha descarta-os e vira <see cref="ReportPublishException"/>.
/// </summary>
public sealed class RabbitMqReportPublisher : IReportPublisher, IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    /// <summary>
    /// Inicializa o publicador.
    /// </summary>
    /// <param name="options">Configuração do RabbitMQ.</param>
    public RabbitMqReportPublisher(IOptions<RabbitMqOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task PublishAsync(Guid reportId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.PublishTimeoutSeconds));

        await _lock.WaitAsync(cancellationToken);

        try
        {
            IChannel channel = await GetChannelAsync(timeout.Token);
            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                MessageId = reportId.ToString(),
                CorrelationId = reportId.ToString(),
                Type = "report.requested",
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            };

            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: _options.QueueName,
                mandatory: true,
                basicProperties: properties,
                body: new ReportMessage(reportId).ToBytes(),
                cancellationToken: timeout.Token);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            await ResetAsync();

            throw new ReportPublishException(
                string.Create(CultureInfo.InvariantCulture, $"Publicação de {reportId} não confirmada: {exception.GetType().Name}."),
                exception);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await ResetAsync();
        _lock.Dispose();
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await ResetAsync();

        _connection = await _options.CreateFactory("expensehub-web").CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        await _options.DeclareQueueAsync(_channel, cancellationToken);

        return _channel;
    }

    private async Task ResetAsync()
    {
        if (_channel is not null)
        {
            try
            {
                await _channel.DisposeAsync();
            }
            catch (Exception exception) when (exception is OperationInterruptedException or AlreadyClosedException)
            {
                // Canal já fechado pelo broker; nada a liberar.
            }

            _channel = null;
        }

        if (_connection is not null)
        {
            try
            {
                await _connection.DisposeAsync();
            }
            catch (Exception exception) when (exception is OperationInterruptedException or AlreadyClosedException)
            {
                // Conexão já fechada; nada a liberar.
            }

            _connection = null;
        }
    }
}
