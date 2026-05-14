using CartShop.Core.Domain;
using CartShop.Events;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events.Dcb;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace CartShop.Core.Feature.Cart.Commands.AddItem;

public record AddItemRequest(string Sku, string DisplayName, int Quantity, decimal UnitPrice);

public static class AddItemEndpoint
{
    [WolverinePost("/api/carts/{cartId}/items")]
    public static async Task<IResult> Handle(
        Guid cartId,
        AddItemRequest request,
        IDocumentSession session,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Sku))
            return Results.BadRequest(new { error = "Sku is required" });

        var sku = new Sku(request.Sku.Trim());

        // Cross-stream invariant boundary: every event affecting this SKU's
        // inventory across every cart. If a matching event is appended between
        // now and SaveChangesAsync, Marten throws DcbConcurrencyException.
        var inventoryQuery = EventTagQuery
            .For(sku)
            .AndEventsOfType<ProductStockSet, ItemAdded, ItemRemoved, CartSubmitted>();

        IEventBoundary<InventoryView> boundary =
            await session.Events.FetchForWritingByTags<InventoryView>(inventoryQuery, ct);

        if (boundary.Aggregate is null)
            return Results.BadRequest(new { error = $"No stock recorded for sku '{request.Sku}'" });

        // Single-stream invariant boundary: cart must exist and be open.
        var cart = await session.LoadAsync<CartAggregate>(cartId, ct);
        if (cart is null) return Results.NotFound();

        try
        {
            boundary.Aggregate.EnsureCanReserve(request.Quantity);
            var evt = cart.AddLine(sku, (request.DisplayName ?? request.Sku).Trim(), request.Quantity, request.UnitPrice);

            var tagged = new Event<ItemAdded>(evt);
            tagged.AddTag(sku);
            session.Events.Append(cartId, tagged);

            await session.SaveChangesAsync(ct);
            return Results.Ok(evt);
        }
        catch (InsufficientStock ex)
        {
            return Results.BadRequest(new { error = "Insufficient stock", sku = ex.Sku, requested = ex.Requested, available = ex.Available });
        }
        catch (InventoryRuleViolation ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (CartAlreadySubmitted ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
        catch (CartRuleViolation ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (DcbConcurrencyException)
        {
            return Results.Conflict(new { error = "Stock changed while reserving; retry." });
        }
    }
}
