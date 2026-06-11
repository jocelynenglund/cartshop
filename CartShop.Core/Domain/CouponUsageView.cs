using CartShop.Events;
using JasperFx.Events.Aggregation;

namespace CartShop.Core.Domain;

public sealed class CouponAlreadyUsed(string code)
    : Exception($"Coupon '{code}' has already been used")
{
    public string Code { get; } = code;
}

// DCB view: every CouponApplied event tagged with a given CouponCode folds
// into one of these. If IsUsed is true, the boundary rejects another claim.
// Note the symmetry with InventoryView — same shape (fields + Apply + an
// invariant-asserting method), different rule. That's the point: DCB is a
// primitive, not a one-trick pattern.
//
// [BoundaryAggregate]: identity-less DCB view (keyed by the CouponCode tag,
// no Id) — required in Marten 9 so the source generator emits its dispatcher.
[BoundaryAggregate]
public class CouponUsageView
{
    public CouponCode? Code { get; set; }
    public Guid? UsedByCart { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public bool IsUsed => UsedByCart.HasValue;

    public void Apply(CouponApplied e)
    {
        Code = e.Code;
        UsedByCart = e.CartId;
        UsedAt = e.At;
    }

    public void EnsureNotUsed()
    {
        if (IsUsed) throw new CouponAlreadyUsed(Code?.Value ?? "?");
    }
}
