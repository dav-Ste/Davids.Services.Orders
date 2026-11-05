namespace Davids.Services.Orders.Application.DTO;

public sealed record OrderItemDto(
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal UnitPrice,
    string Currency,
    int Quantity,
    decimal LineTotal);

public sealed record OrderSummaryDto(
    Guid Id,
    Guid CustomerId,
    string? CustomerEmail,
    string ShippingFullName,
    decimal TotalAmount,
    string Currency,
    int ItemCount,
    string Status,
    string PaymentMethod,
    string CardLast4,
    string? PaymentReference,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public sealed record OrderDetailsDto(
    Guid Id,
    Guid CustomerId,
    string? CustomerEmail,
    string ShippingFullName,
    string ShippingAddress,
    decimal TotalAmount,
    string Currency,
    string Status,
    string PaymentMethod,
    string CardLast4,
    Guid? PaymentId,
    string? PaymentReference,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? CompletedAt,
    IReadOnlyList<OrderItemDto> Items);

public sealed record BasketSnapshotLine(Guid ProductId, int Quantity);

public sealed record VerifiedProductSnapshot(
    Guid Id,
    string Sku,
    string Name,
    decimal Price,
    string Currency,
    int StockQuantity,
    bool IsActive);
