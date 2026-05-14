using CartShop.Events;

namespace CartShop.Core.Domain;

public enum CartStatus { Open, Submitted }

public record CartLine(Guid ItemId, Sku Sku, string DisplayName, int Quantity, decimal UnitPrice)
{
    public decimal LineTotal => Quantity * UnitPrice;
}

public class CartRuleViolation(string message) : Exception(message);

public sealed class CartAlreadySubmitted() : CartRuleViolation("Cart already submitted");

public sealed class CustomerAlreadyHasOpenCart(string customerName)
    : CartRuleViolation($"Customer '{customerName}' already has an open cart")
{
    public string CustomerName { get; } = customerName;
}

public sealed class ItemNotInCart(Guid itemId)
    : CartRuleViolation($"Item {itemId} not in cart")
{
    public Guid ItemId { get; } = itemId;
}

public class CartAggregate
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = "";
    public CartStatus Status { get; set; } = CartStatus.Open;
    public List<CartLine> Lines { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public CouponCode? AppliedCoupon { get; set; }
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

    public void Apply(CouponApplied e)
    {
        AppliedCoupon = e.Code;
    }

    public static CartCreated Create(Guid cartId, string customerName)
    {
        if (string.IsNullOrWhiteSpace(customerName))
            throw new CartRuleViolation("CustomerName is required");
        return new CartCreated(cartId, customerName.Trim(), DateTimeOffset.UtcNow);
    }

    public ItemAdded AddLine(Sku sku, string displayName, int quantity, decimal unitPrice)
    {
        if (Status == CartStatus.Submitted) throw new CartAlreadySubmitted();
        if (quantity <= 0)                  throw new CartRuleViolation("Quantity must be > 0");
        if (unitPrice < 0)                  throw new CartRuleViolation("UnitPrice must be >= 0");

        return new ItemAdded(Id, Guid.NewGuid(), sku, displayName, quantity, unitPrice);
    }

    public ItemRemoved RemoveLine(Guid itemId)
    {
        if (Status == CartStatus.Submitted) throw new CartAlreadySubmitted();

        var line = Lines.FirstOrDefault(l => l.ItemId == itemId)
            ?? throw new ItemNotInCart(itemId);

        return new ItemRemoved(Id, itemId, line.Sku, line.Quantity);
    }

    public CartSubmitted Submit()
    {
        if (Status == CartStatus.Submitted) throw new CartAlreadySubmitted();
        if (Lines.Count == 0)               throw new CartRuleViolation("Cannot submit an empty cart");

        return new CartSubmitted(Id, DateTimeOffset.UtcNow, Total);
    }

    public CouponApplied ApplyCoupon(CouponCode code)
    {
        if (Status == CartStatus.Submitted) throw new CartAlreadySubmitted();
        if (AppliedCoupon is not null)
            throw new CartRuleViolation($"Cart already has coupon '{AppliedCoupon.Value}' applied");

        return new CouponApplied(Id, code, DateTimeOffset.UtcNow);
    }
}
