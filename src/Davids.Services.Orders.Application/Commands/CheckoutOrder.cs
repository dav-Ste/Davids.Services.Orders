namespace Davids.Services.Orders.Application.Commands;

public sealed record CheckoutOrder(
    string ShippingFullName,
    string ShippingAddress,
    string? PaymentMethod = "card",
    string? CardLast4 = "4242");
