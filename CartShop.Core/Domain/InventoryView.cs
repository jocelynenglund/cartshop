using CartShop.Events;
using JasperFx.Events.Aggregation;

namespace CartShop.Core.Domain;

public class InventoryRuleViolation(string message) : Exception(message);

public sealed class InsufficientStock(string sku, int requested, int available)
    : InventoryRuleViolation($"Insufficient stock for {sku}: requested {requested}, available {available}")
{
    public string Sku { get; } = sku;
    public int Requested { get; } = requested;
    public int Available { get; } = available;
}

// Live fold built by Marten via tag-based aggregation (DCB).
//
// Bookkeeping:
//   Stock              — physical units currently on hand
//   ReservedByCart     — per-cart pending reservations (open carts only)
//   Reserved           — sum of pending reservations
//   Available          — Stock - Reserved
//
// Lifecycle of one unit:
//   ProductStockSet    Stock = n            (n on the shelf)
//   ItemAdded          Reserved += qty      (held but not paid)
//   ItemRemoved        Reserved -= qty      (released back to available)
//   CartSubmitted      Stock -= qty,        (sold; comes out of stock,
//                      Reserved -= qty       no longer counts as held)
//
// CartSubmitted is per-cart and per-Sku because the cart can hold multiple
// SKUs. The SubmitCart handler tags the CartSubmitted event with every Sku
// in the cart so each SKU's DCB fold sees the same event.
//
// [BoundaryAggregate]: Marten 9 dispatches Apply methods via a compile-time
// source generator. This view is identity-less (keyed by the Sku tag, no Id
// property), so it needs this explicit marker for the generator to emit a
// dispatcher — without it FetchForWritingByTags<InventoryView> throws
// InvalidProjectionException. The Sku tag is wired to it in Initialization.
[BoundaryAggregate]
public class InventoryView
{
    public Sku? Sku { get; set; }
    public int Stock { get; set; }
    public Dictionary<Guid, int> ReservedByCart { get; set; } = new();
    public int Reserved => ReservedByCart.Values.Sum();
    public int Available => Stock - Reserved;

    public void Apply(ProductStockSet e)
    {
        Sku = e.Sku;
        Stock = e.Quantity;
    }

    public void Apply(ItemAdded e)
    {
        Sku ??= e.Sku;
        ReservedByCart[e.CartId] = ReservedByCart.GetValueOrDefault(e.CartId) + e.Quantity;
    }

    public void Apply(ItemRemoved e)
    {
        Sku ??= e.Sku;
        var next = ReservedByCart.GetValueOrDefault(e.CartId) - e.Quantity;
        if (next > 0) ReservedByCart[e.CartId] = next;
        else          ReservedByCart.Remove(e.CartId);
    }

    public void Apply(CartSubmitted e)
    {
        // Reservation becomes consumption: deduct the cart's pending qty for
        // this Sku from Stock and clear it from Reserved. Net effect on
        // Available is zero — but the books are honest: Stock now reflects
        // what's physically left, Reserved only what's still pending.
        if (ReservedByCart.TryGetValue(e.CartId, out var qty))
        {
            Stock -= qty;
            ReservedByCart.Remove(e.CartId);
        }
    }

    public void EnsureCanReserve(int quantity)
    {
        if (Sku is null)          throw new InventoryRuleViolation("No stock recorded");
        if (quantity <= 0)        throw new InventoryRuleViolation("Quantity must be > 0");
        if (Available < quantity) throw new InsufficientStock(Sku.Value, quantity, Available);
    }
}
