using System.Text;
using System.Text.Json;
using Davids.Services.Orders.Application.Events;
using Davids.Services.Orders.Application.Services;
using Davids.Services.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Davids.Services.Orders.Infrastructure.Messaging;

public sealed class OrdersMessageWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<OrdersMessageWorker> logger) : BackgroundService
{
    private const string PaymentCapturedQueue = "orders-service/payments.payment_captured";
    private const string PaymentFailedQueue = "orders-service/payments.payment_failed";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Orders RabbitMQ worker disconnected; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var connectionString = configuration.GetConnectionString("messaging")
            ?? throw new InvalidOperationException("Missing RabbitMQ messaging connection string.");
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var publisher = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            ct);
        await using var consumerChannel = await connection.CreateChannelAsync(cancellationToken: ct);

        await publisher.ExchangeDeclareAsync("orders", ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await consumerChannel.ExchangeDeclareAsync("payments", ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await consumerChannel.ExchangeDeclareAsync("orders.dead", ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await consumerChannel.QueueDeclareAsync("orders-service.dead", durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await consumerChannel.QueueBindAsync("orders-service.dead", "orders.dead", "#", cancellationToken: ct);

        var dlxArgs = new Dictionary<string, object?> { ["x-dead-letter-exchange"] = "orders.dead" };
        await consumerChannel.QueueDeclareAsync(PaymentCapturedQueue, durable: true, exclusive: false, autoDelete: false, arguments: dlxArgs, cancellationToken: ct);
        await consumerChannel.QueueBindAsync(PaymentCapturedQueue, "payments", "payment_captured", cancellationToken: ct);
        await consumerChannel.QueueDeclareAsync(PaymentFailedQueue, durable: true, exclusive: false, autoDelete: false, arguments: dlxArgs, cancellationToken: ct);
        await consumerChannel.QueueBindAsync(PaymentFailedQueue, "payments", "payment_failed", cancellationToken: ct);
        await consumerChannel.BasicQosAsync(0, 1, global: false, ct);

        var capturedConsumer = new AsyncEventingBasicConsumer(consumerChannel);
        capturedConsumer.ReceivedAsync += (_, args) => ProcessAsync<PaymentCaptured>(
            args, consumerChannel, (svc, evt, token) => svc.ApplyPaymentCapturedAsync(evt, token), ct);
        await consumerChannel.BasicConsumeAsync(PaymentCapturedQueue, autoAck: false, capturedConsumer, ct);

        var failedConsumer = new AsyncEventingBasicConsumer(consumerChannel);
        failedConsumer.ReceivedAsync += (_, args) => ProcessAsync<PaymentFailed>(
            args, consumerChannel, (svc, evt, token) => svc.ApplyPaymentFailedAsync(evt, token), ct);
        await consumerChannel.BasicConsumeAsync(PaymentFailedQueue, autoAck: false, failedConsumer, ct);

        while (!ct.IsCancellationRequested)
        {
            await PublishPendingOutboxAsync(publisher, ct);
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }
    }

    private async Task ProcessAsync<TEvent>(
        BasicDeliverEventArgs args,
        IChannel channel,
        Func<IOrdersService, TEvent, CancellationToken, Task> handler,
        CancellationToken ct)
    {
        var messageId = args.BasicProperties.MessageId;
        if (string.IsNullOrWhiteSpace(messageId))
        {
            messageId = $"{args.RoutingKey}:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(args.Body.Span))[..24]}";
        }

        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            if (!await db.Inbox.AnyAsync(x => x.Id == messageId, ct))
            {
                var evt = JsonSerializer.Deserialize<TEvent>(args.Body.Span, JsonOptions);
                if (evt is not null)
                {
                    var svc = scope.ServiceProvider.GetRequiredService<IOrdersService>();
                    await handler(svc, evt, ct);
                }

                db.Inbox.Add(new InboxMessage { Id = messageId, ReceivedAt = DateTime.UtcNow });
                await db.SaveChangesAsync(ct);
            }

            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed processing {RoutingKey} message {MessageId} in Orders.", args.RoutingKey, messageId);
            await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, ct);
        }
    }

    private async Task PublishPendingOutboxAsync(IChannel publisher, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var pending = await db.Outbox
            .Where(x => x.PublishedAt == null)
            .OrderBy(x => x.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        foreach (var msg in pending)
        {
            var props = new BasicProperties
            {
                MessageId = msg.Id,
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Type = msg.Type
            };
            await publisher.BasicPublishAsync(
                exchange: "orders",
                routingKey: msg.Type,
                mandatory: false,
                basicProperties: props,
                body: Encoding.UTF8.GetBytes(msg.Payload),
                cancellationToken: ct);
            msg.PublishedAt = DateTime.UtcNow;
        }

        if (pending.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
