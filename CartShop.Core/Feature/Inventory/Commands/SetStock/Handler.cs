using CartShop.Events;
using JasperFx.Events;
using Marten;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace CartShop.Core.Feature.Inventory.Commands.SetStock;

public record SetStockRequest(int Quantity);

public static class SetStockEndpoint
{
    // Single shared stream for all inventory events. With DCB, the Sku tag —
    // not the stream — is the consistency boundary, so the stream id here is
    // just a write buffer.
    private static readonly Guid InventoryStreamId = new("11111111-1111-1111-1111-111111111111");

    [WolverinePost("/api/inventory/{sku}")]
    public static async Task<IResult> Handle(
        string sku,
        SetStockRequest request,
        IDocumentSession session,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sku))
            return Results.BadRequest(new { error = "sku is required" });
        if (request.Quantity < 0)
            return Results.BadRequest(new { error = "quantity must be >= 0" });

        var evt = new ProductStockSet(new Sku(sku), request.Quantity, DateTimeOffset.UtcNow);

        // Wrap into IEvent so we can attach a DCB tag explicitly. Marten reads
        // the tags from the IEvent metadata when persisting (regular Append
        // doesn't fire tag-property inference).
        var tagged = new Event<ProductStockSet>(evt);
        tagged.AddTag(new Sku(sku));

        var existing = await session.Events.FetchStreamStateAsync(InventoryStreamId, ct);
        if (existing is null) session.Events.StartStream(InventoryStreamId, tagged);
        else session.Events.Append(InventoryStreamId, tagged);

        await session.SaveChangesAsync(ct);

        return Results.Ok(new { sku, stock = request.Quantity });
    }
}
