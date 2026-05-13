namespace CartShop.Events;

public record CartCreated(Guid CartId, string CustomerName, DateTimeOffset CreatedAt);

// Sku and Quantity are part of the event so the inventory DCB projection
// can fold reservations across all carts without joining back to the cart stream.
public record ItemAdded(
    Guid CartId,
    Guid ItemId,
    Sku Sku,
    string DisplayName,
    int Quantity,
    decimal UnitPrice);

// Mirrors ItemAdded so the inventory projection can undo the reservation.
public record ItemRemoved(Guid CartId, Guid ItemId, Sku Sku, int Quantity);

public record CartSubmitted(Guid CartId, DateTimeOffset SubmittedAt, decimal TotalAmount);
