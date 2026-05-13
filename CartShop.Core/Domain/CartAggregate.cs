using CartShop.Events;

namespace CartShop.Core.Domain;

public enum CartStatus { Open, Submitted }

public record CartLine(Guid ItemId, Sku Sku, string DisplayName, int Quantity, decimal UnitPrice)
{
    public decimal LineTotal => Quantity * UnitPrice;
}

public class CartAggregate
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = "";
    public CartStatus Status { get; set; } = CartStatus.Open;
    public List<CartLine> Lines { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public decimal Total => Lines.Sum(l => l.LineTotal);

    public void Apply(CartCreated e)
    {
        Id = e.CartId;
        CustomerName = e.CustomerName;
        CreatedAt = e.CreatedAt;
        Status = CartStatus.Open;
    }

    public void Apply(ItemAdded e)
    {
        Lines.Add(new CartLine(e.ItemId, e.Sku, e.DisplayName, e.Quantity, e.UnitPrice));
    }

    public void Apply(ItemRemoved e)
    {
        Lines.RemoveAll(l => l.ItemId == e.ItemId);
    }

    public void Apply(CartSubmitted e)
    {
        Status = CartStatus.Submitted;
        SubmittedAt = e.SubmittedAt;
    }
}
