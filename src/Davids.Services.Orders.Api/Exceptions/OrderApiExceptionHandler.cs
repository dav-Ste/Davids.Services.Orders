using Davids.Services.Orders.Core.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace Davids.Services.Orders.Api.Exceptions;

public sealed class OrderApiExceptionHandler(ILogger<OrderApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domain)
        {
            return false;
        }

        var status = domain switch
        {
            OrderNotFoundException => StatusCodes.Status404NotFound,
            OrderItemUnavailableException => StatusCodes.Status409Conflict,
            CannotCancelOrderException => StatusCodes.Status409Conflict,
            EmptyCheckoutBasketException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status400BadRequest
        };

        logger.LogWarning("Orders domain error {Code}: {Message}", domain.Code, domain.Message);
        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new
        {
            code = domain.Code,
            reason = domain.Message
        }, cancellationToken);
        return true;
    }
}
