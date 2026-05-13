using CartShop.Core.Domain;
using CartShop.Events;
using JasperFx.Events;
using Marten;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace CartShop.Core.Feature.Cart.Commands.RemoveItem;

public static class RemoveItemEndpoint
{
    [WolverineDelete("/api/carts/{cartId}/items/{itemId}")]
    public static async Task<IResult> Handle(
        Guid cartId,
        Guid itemId,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Need the original Sku + Quantity on the ItemRemoved event so the
        // inventory projection can undo the reservation. Read them off the cart
        // aggregate (snapshot kept inline by Marten).
        var cart = await session.LoadAsync<CartAggregate>(cartId, ct);
        if (cart is null) return Results.NotFound();

        var line = cart.Lines.FirstOrDefault(l => l.ItemId == itemId);
        if (line is null) return Results.NotFound(new { error = "Item not in cart" });

        var evt = new ItemRemoved(cartId, itemId, line.Sku, line.Quantity);

        var tagged = new Event<ItemRemoved>(evt);
        tagged.AddTag(line.Sku);

        session.Events.Append(cartId, tagged);
        await session.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
