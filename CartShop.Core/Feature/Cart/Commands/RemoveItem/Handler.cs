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
        // Load the cart snapshot so the aggregate can produce ItemRemoved with
        // the original Sku + Quantity (needed for inventory to undo the reservation).
        var cart = await session.LoadAsync<CartAggregate>(cartId, ct);
        if (cart is null) return Results.NotFound();

        try
        {
            var evt = cart.RemoveLine(itemId);

            var tagged = new Event<ItemRemoved>(evt);
            tagged.AddTag(evt.Sku);

            session.Events.Append(cartId, tagged);
            await session.SaveChangesAsync(ct);
            return Results.NoContent();
        }
        catch (ItemNotInCart ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (CartAlreadySubmitted ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
        catch (CartRuleViolation ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
