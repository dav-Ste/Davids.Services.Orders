using System.Text.Json.Serialization;

namespace Davids.Services.Orders.Application.Commands;

public sealed record CheckoutOrder(
    [property: JsonRequired] string ShippingFullName,
    [property: JsonRequired] string ShippingAddress,
    string? PaymentMethod = "card",
    string? CardLast4 = "4242");
