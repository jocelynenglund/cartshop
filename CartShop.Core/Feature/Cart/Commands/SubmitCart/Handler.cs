using CartShop.Core.Domain;
using CartShop.Events;
using Marten;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace CartShop.Core.Feature.Cart.Commands.SubmitCart;

public record SubmitCartResponse(Guid CartId, decimal TotalAmount, DateTimeOffset SubmittedAt);

public static class SubmitCartEndpoint
{
    [WolverinePost("/api/carts/{cartId}/submit")]
    public static async Task<IResult> Handle(
        Guid cartId,
        IDocumentSession session,
        CancellationToken ct)
    {
        var cart = await session.Events.AggregateStreamAsync<CartAggregate>(cartId, token: ct);
        if (cart is null) return Results.NotFound();
        if (cart.Status == CartStatus.Submitted)
            return Results.Conflict(new { error = "Cart already submitted" });
        if (cart.Lines.Count == 0)
            return Results.BadRequest(new { error = "Cannot submit an empty cart" });

        var evt = new CartSubmitted(cartId, DateTimeOffset.UtcNow, cart.Total);
        session.Events.Append(cartId, evt);
        await session.SaveChangesAsync(ct);

        return Results.Ok(new SubmitCartResponse(cartId, evt.TotalAmount, evt.SubmittedAt));
    }
}
