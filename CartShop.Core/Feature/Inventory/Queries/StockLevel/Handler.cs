using CartShop.Core.Domain;
using CartShop.Events;
using JasperFx.Events.Tags;
using Marten;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace CartShop.Core.Feature.Inventory.Queries.StockLevel;

public record StockLevelResponse(string Sku, int Stock, int Reserved, int Available);

public static class StockLevelEndpoint
{
    [WolverineGet("/api/inventory/{sku}")]
    public static async Task<IResult> Handle(
        string sku,
        IDocumentSession session,
        CancellationToken ct)
    {
        var query = EventTagQuery
            .For(new Sku(sku))
            .AndEventsOfType<ProductStockSet, ItemAdded, ItemRemoved, CartSubmitted>();

        var view = await session.Events.AggregateByTagsAsync<InventoryView>(query, ct);
        if (view is null || view.Sku is null)
            return Results.NotFound(new { error = $"No stock recorded for sku '{sku}'" });

        return Results.Ok(new StockLevelResponse(sku, view.Stock, view.Reserved, view.Available));
    }
}
