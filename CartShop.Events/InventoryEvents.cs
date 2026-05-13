namespace CartShop.Events;

// Sets (or resets) the total stock for a SKU. Written to its own stream
// (`product-{sku}`) but tagged with Sku for DCB queries.
public record ProductStockSet(Sku Sku, int Quantity, DateTimeOffset At);
