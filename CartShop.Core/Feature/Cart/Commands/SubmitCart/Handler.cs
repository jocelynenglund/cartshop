using CartShop.Core.Domain;
using CartShop.Events;
using JasperFx.Events;
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

        try
        {
            var evt = cart.Submit();

            // Tag CartSubmitted with every Sku in the cart so each Sku's
            // InventoryView fold sees the submission and moves the cart's
            // pending reservation from Reserved into a Stock deduction.
            var tagged = new Event<CartSubmitted>(evt);
            foreach (var sku in cart.Lines.Select(l => l.Sku).Distinct())
            {
                tagged.AddTag(sku);
            }

            session.Events.Append(cartId, tagged);
            await session.SaveChangesAsync(ct);
            return Results.Ok(new SubmitCartResponse(cartId, evt.TotalAmount, evt.SubmittedAt));
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
