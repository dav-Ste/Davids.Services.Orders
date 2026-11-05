namespace Davids.Services.Orders.Application.Events;

public sealed record OrderPlacedItem(
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal UnitPrice,
    int Quantity);

public sealed record OrderPlaced(
    Guid OrderId,
    Guid CustomerId,
    string? CustomerEmail,
    decimal TotalAmount,
    string Currency,
    string PaymentMethod,
    string CardLast4,
    IReadOnlyList<OrderPlacedItem> Items);

public sealed record CompletedOrderItem(Guid ProductId, int Quantity);

public sealed record OrderCompleted(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<CompletedOrderItem> Items);

public sealed record PaymentCaptured(
    Guid PaymentId,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string ProviderReference);

public sealed record PaymentFailed(
    Guid PaymentId,
    Guid OrderId,
    Guid CustomerId,
    string Reason);
