using CartShop.Events;
using Marten.Events.Aggregation;

namespace CartShop.Core.Domain;

// Lookup document: while a customer has an open cart, there's a row keyed by
// the cart id. When the cart submits, the row is deleted. The point is to
// enforce "one open cart per customer" — CreateCart queries this collection
// before starting a new stream, and that query MUST see the doc written by
// the previous CreateCart commit. Hence: inline lifecycle (write-path cost,
// strong consistency).
public class OpenCartByCustomer
{
    public Guid Id { get; set; }                       // cart id
    public string CustomerName { get; set; } = "";
    public string CustomerNameNormalized { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }

    public static string Normalize(string name) => (name ?? "").Trim().ToLowerInvariant();
}

// SingleStreamProjection because one cart stream = one OpenCartByCustomer
// row (while open). CartCreated produces it; CartSubmitted deletes it.
// `partial` required by Marten 9: convention-method projection subclasses
// (Create/Apply/ShouldDelete) are dispatched by a compile-time source
// generator, not runtime codegen, and the generator emits the other half
// of this class.
public partial class OpenCartByCustomerProjection : SingleStreamProjection<OpenCartByCustomer, Guid>
{
    public OpenCartByCustomerProjection()
    {
        DeleteEvent<CartSubmitted>();
    }

    public OpenCartByCustomer Create(CartCreated e) => new()
    {
        Id = e.CartId,
        CustomerName = e.CustomerName,
        CustomerNameNormalized = OpenCartByCustomer.Normalize(e.CustomerName),
        CreatedAt = e.CreatedAt,
    };
}
