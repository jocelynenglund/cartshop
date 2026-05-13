using CartShop.Events;

namespace CartShop.Core.Domain;

// Live fold built by Marten via tag-based aggregation (DCB).
// Stock is set by ProductStockSet; Reserved is the running balance of
// ItemAdded - ItemRemoved across every cart for this Sku. Reserved counts
// both open carts (pending) and submitted carts (sold) — once a SKU is
// "spoken for" by a cart it stays out of available stock until removed.
public class InventoryView
{
    public Sku? Sku { get; set; }
    public int Stock { get; set; }
    public int Reserved { get; set; }
    public int Available => Stock - Reserved;

    public void Apply(ProductStockSet e)
    {
        Sku = e.Sku;
        Stock = e.Quantity;
    }

    public void Apply(ItemAdded e)
    {
        Sku ??= e.Sku;
        Reserved += e.Quantity;
    }

    public void Apply(ItemRemoved e)
    {
        Sku ??= e.Sku;
        Reserved -= e.Quantity;
    }
}
