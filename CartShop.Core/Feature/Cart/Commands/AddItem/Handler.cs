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
        if (request.Quantity <= 0) return Results.BadRequest(new { error = "Quantity must be > 0" });
        if (request.UnitPrice < 0) return Results.BadRequest(new { error = "UnitPrice must be >= 0" });
        if (string.IsNullOrWhiteSpace(request.Sku)) return Results.BadRequest(new { error = "Sku is required" });

        var sku = new Sku(request.Sku.Trim());

        // Build a tag query covering every event that affects this SKU's
        // inventory: stock changes, additions, removals — across every cart.
        var inventoryQuery = EventTagQuery
            .For(sku)
            .AndEventsOfType<ProductStockSet, ItemAdded, ItemRemoved>();

        // Fold those events into an InventoryView and establish a DCB write
        // boundary. If any matching event is appended between now and
        // SaveChangesAsync, Marten throws DcbConcurrencyException.
        IEventBoundary<InventoryView> boundary =
            await session.Events.FetchForWritingByTags<InventoryView>(inventoryQuery, ct);

        var inventory = boundary.Aggregate;
        if (inventory is null || inventory.Sku is null)
            return Results.BadRequest(new { error = $"No stock recorded for sku '{request.Sku}'" });

        if (inventory.Available < request.Quantity)
            return Results.BadRequest(new
            {
                error = "Insufficient stock",
                sku = sku.Value,
                requested = request.Quantity,
                available = inventory.Available
            });

        var evt = new ItemAdded(
            cartId,
            Guid.NewGuid(),
            sku,
            (request.DisplayName ?? request.Sku).Trim(),
            request.Quantity,
            request.UnitPrice);

        // Wrap in IEvent so we can attach the Sku as a DCB tag. The tag row is
        // written in the same INSERT batch as the event row, which is what
        // makes the SaveChangesAsync consistency check correct.
        var tagged = new Event<ItemAdded>(evt);
        tagged.AddTag(sku);

        session.Events.Append(cartId, tagged);

        try
        {
            await session.SaveChangesAsync(ct);
        }
        catch (DcbConcurrencyException)
        {
            return Results.Conflict(new { error = "Stock changed while reserving; retry." });
        }

        return Results.Ok(evt);
    }
}
