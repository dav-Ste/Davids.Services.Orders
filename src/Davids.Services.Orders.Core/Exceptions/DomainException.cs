namespace Davids.Services.Orders.Core.Exceptions;

public abstract class DomainException(string message) : Exception(message)
{
    public abstract string Code { get; }
}

public sealed class OrderNotFoundException(Guid orderId)
    : DomainException($"Order '{orderId}' was not found.")
{
    public override string Code => "order_not_found";
}

public sealed class EmptyCheckoutBasketException()
    : DomainException("Cannot check out with an empty shopping basket.")
{
    public override string Code => "empty_checkout_basket";
}

public sealed class InvalidOrderCheckoutException(string message)
    : DomainException(message)
{
    public override string Code => "invalid_order_checkout";
}

public sealed class OrderItemUnavailableException(string message)
    : DomainException(message)
{
    public override string Code => "order_item_unavailable";
}

public sealed class CannotCancelOrderException(Guid orderId, string status)
    : DomainException($"Order '{orderId}' in status '{status}' can no longer be cancelled.")
{
    public override string Code => "cannot_cancel_order";
}
