using Davids.Services.Orders.Application.Commands;
using Davids.Services.Orders.Application.DTO;
using Davids.Services.Orders.Application.Events;
using Davids.Services.Orders.Core.Entities;
using Davids.Services.Orders.Core.Exceptions;
using Davids.Services.Orders.Core.Repositories;

namespace Davids.Services.Orders.Application.Services;

public interface IOrdersService
{
    Task<OrderDetailsDto> CheckoutAsync(Guid customerId, string? customerEmail, string? customerBearerToken, CheckoutOrder command, CancellationToken ct = default);
    Task<IReadOnlyList<OrderSummaryDto>> GetCustomerOrdersAsync(Guid customerId, int limit = 50, CancellationToken ct = default);
    Task<OrderDetailsDto?> GetCustomerOrderAsync(Guid customerId, Guid orderId, CancellationToken ct = default);
    Task<OrderDetailsDto> CancelCustomerOrderAsync(Guid customerId, Guid orderId, CancellationToken ct = default);
    Task<IReadOnlyList<OrderSummaryDto>> SearchWorkforceAsync(string? search, string? status, Guid? customerId, int limit = 100, CancellationToken ct = default);
    Task<OrderDetailsDto?> GetWorkforceOrderAsync(Guid orderId, CancellationToken ct = default);
    Task ApplyPaymentCapturedAsync(PaymentCaptured evt, CancellationToken ct = default);
    Task ApplyPaymentFailedAsync(PaymentFailed evt, CancellationToken ct = default);
}

public sealed class OrdersService(
    IOrderRepository repository,
    IBasketCheckoutClient basketClient,
    IProductCatalogVerifier catalogVerifier,
    IOutboxWriter outbox) : IOrdersService
{
    public async Task<OrderDetailsDto> CheckoutAsync(
        Guid customerId,
        string? customerEmail,
        string? customerBearerToken,
        CheckoutOrder command,
        CancellationToken ct = default)
    {
        var basketLines = await basketClient.GetCustomerBasketLinesAsync(customerId, customerBearerToken, ct);
        if (basketLines.Count == 0)
        {
            throw new EmptyCheckoutBasketException();
        }

        var orderId = Guid.NewGuid();
        var orderItems = new List<OrderItem>(basketLines.Count);

        foreach (var line in basketLines)
        {
            var product = await catalogVerifier.GetProductAsync(line.ProductId, ct);
            if (product is null || !product.IsActive)
            {
                throw new OrderItemUnavailableException($"Product '{line.ProductId}' is no longer available.");
            }

            if (line.Quantity > product.StockQuantity)
            {
                throw new OrderItemUnavailableException(
                    $"Product '{product.Name}' only has {product.StockQuantity} units remaining in stock.");
            }

            orderItems.Add(new OrderItem(
                orderId,
                product.Id,
                product.Sku,
                product.Name,
                product.Price,
                product.Currency,
                line.Quantity));
        }

        var now = DateTime.UtcNow;
        var order = new Order(
            orderId,
            customerId,
            customerEmail,
            command.ShippingFullName,
            command.ShippingAddress,
            command.PaymentMethod,
            command.CardLast4,
            orderItems,
            now);

        await repository.AddAsync(order, ct);
        outbox.Enqueue("order_placed", new OrderPlaced(
            order.Id,
            order.CustomerId,
            order.CustomerEmail,
            order.TotalAmount,
            order.Currency,
            order.PaymentMethod,
            order.CardLast4,
            order.Items.Select(i => new OrderPlacedItem(i.ProductId, i.Sku, i.ProductName, i.UnitPrice, i.Quantity)).ToArray()));
        await repository.SaveChangesAsync(ct);

        await basketClient.ClearCustomerBasketAsync(customerId, customerBearerToken, ct);
        return MapDetails(order);
    }

    public async Task<IReadOnlyList<OrderSummaryDto>> GetCustomerOrdersAsync(Guid customerId, int limit = 50, CancellationToken ct = default)
    {
        var orders = await repository.GetByCustomerAsync(customerId, Math.Clamp(limit, 1, 200), ct);
        return orders.Select(MapSummary).ToArray();
    }

    public async Task<OrderDetailsDto?> GetCustomerOrderAsync(Guid customerId, Guid orderId, CancellationToken ct = default)
    {
        var order = await repository.GetAsync(orderId, ct);
        return order is not null && order.CustomerId == customerId ? MapDetails(order) : null;
    }

    public async Task<OrderDetailsDto> CancelCustomerOrderAsync(Guid customerId, Guid orderId, CancellationToken ct = default)
    {
        var order = await repository.GetAsync(orderId, ct);
        if (order is null || order.CustomerId != customerId)
        {
            throw new OrderNotFoundException(orderId);
        }

        order.Cancel(DateTime.UtcNow);
        await repository.SaveChangesAsync(ct);
        return MapDetails(order);
    }

    public async Task<IReadOnlyList<OrderSummaryDto>> SearchWorkforceAsync(
        string? search,
        string? status,
        Guid? customerId,
        int limit = 100,
        CancellationToken ct = default)
    {
        OrderStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<OrderStatus>(status.Trim(), ignoreCase: true, out var st))
        {
            parsedStatus = st;
        }

        var orders = await repository.SearchAsync(search, parsedStatus, customerId, Math.Clamp(limit, 1, 500), ct);
        return orders.Select(MapSummary).ToArray();
    }

    public async Task<OrderDetailsDto?> GetWorkforceOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await repository.GetAsync(orderId, ct);
        return order is null ? null : MapDetails(order);
    }

    public async Task ApplyPaymentCapturedAsync(PaymentCaptured evt, CancellationToken ct = default)
    {
        var order = await repository.GetAsync(evt.OrderId, ct);
        if (order is null)
        {
            return;
        }

        if (order.MarkPaymentCaptured(evt.PaymentId, evt.ProviderReference, DateTime.UtcNow))
        {
            outbox.Enqueue("order_completed", new OrderCompleted(
                order.Id,
                order.CustomerId,
                order.Items.Select(i => new CompletedOrderItem(i.ProductId, i.Quantity)).ToArray()));
            await repository.SaveChangesAsync(ct);
        }
    }

    public async Task ApplyPaymentFailedAsync(PaymentFailed evt, CancellationToken ct = default)
    {
        var order = await repository.GetAsync(evt.OrderId, ct);
        if (order is null)
        {
            return;
        }

        order.MarkPaymentFailed(evt.PaymentId, evt.Reason, DateTime.UtcNow);
        await repository.SaveChangesAsync(ct);
    }

    private static OrderSummaryDto MapSummary(Order o) => new(
        o.Id,
        o.CustomerId,
        o.CustomerEmail,
        o.ShippingFullName,
        o.TotalAmount,
        o.Currency,
        o.Items.Sum(x => x.Quantity),
        FormatStatus(o.Status),
        o.PaymentMethod,
        o.CardLast4,
        o.PaymentReference,
        o.FailureReason,
        o.CreatedAt,
        o.CompletedAt);

    private static OrderDetailsDto MapDetails(Order o) => new(
        o.Id,
        o.CustomerId,
        o.CustomerEmail,
        o.ShippingFullName,
        o.ShippingAddress,
        o.TotalAmount,
        o.Currency,
        FormatStatus(o.Status),
        o.PaymentMethod,
        o.CardLast4,
        o.PaymentId,
        o.PaymentReference,
        o.FailureReason,
        o.CreatedAt,
        o.UpdatedAt,
        o.CompletedAt,
        o.Items.Select(i => new OrderItemDto(i.ProductId, i.Sku, i.ProductName, i.UnitPrice, i.Currency, i.Quantity, i.LineTotal)).ToArray());

    private static string FormatStatus(OrderStatus status) => status switch
    {
        OrderStatus.PendingPayment => "pending_payment",
        OrderStatus.Completed => "completed",
        OrderStatus.PaymentFailed => "payment_failed",
        OrderStatus.Cancelled => "cancelled",
        _ => "pending_payment"
    };
}
