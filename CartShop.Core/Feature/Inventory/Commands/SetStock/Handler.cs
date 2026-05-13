using System.Security.Cryptography;
using System.Text;
using CartShop.Events;
using JasperFx.Events;
using Marten;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace CartShop.Core.Feature.Inventory.Commands.SetStock;

public record SetStockRequest(int Quantity);

public static class SetStockEndpoint
{
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

        var skuValue = new Sku(sku);
        var streamId = ProductStreamId(sku);
        var evt = new ProductStockSet(skuValue, request.Quantity, DateTimeOffset.UtcNow);

        // Wrap into IEvent so we can attach a DCB tag explicitly. Marten reads
        // the tags from the IEvent metadata when persisting (regular Append
        // doesn't fire tag-property inference).
        var tagged = new Event<ProductStockSet>(evt);
        tagged.AddTag(skuValue);

        var existing = await session.Events.FetchStreamStateAsync(streamId, ct);
        if (existing is null) session.Events.StartStream(streamId, tagged);
        else session.Events.Append(streamId, tagged);

        await session.SaveChangesAsync(ct);

        return Results.Ok(new { sku, stock = request.Quantity });
    }

    // Deterministic Guid per SKU so SetStock for the same SKU always hits the
    // same stream.
    internal static Guid ProductStreamId(string sku)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("product:" + sku));
        return new Guid(bytes.AsSpan(0, 16).ToArray());
    }
}
