using CartShop.Events;
using Marten.Events.Projections;

namespace CartShop.Core.Domain;

// Read model: one document per UTC day with running totals across every cart.
// Updated by an *async* projection — the daemon polls committed events and
// rolls them up in the background. Tolerates lag; never used to enforce
// invariants. Reads of stale buckets are fine because "yesterday's revenue"
// doesn't depend on what happened in the last 500ms.
public class SalesByDay
{
    // YYYY-MM-DD — used as the document Id and the grouping key.
    public string Id { get; set; } = "";
    public int CartCount { get; set; }
    public decimal TotalRevenue { get; set; }
}

// Slices CartSubmitted events into per-day buckets and folds totals into each
// bucket. MultiStreamProjection because the identity (day) is derived from
// event data, not from the source stream id — many cart streams contribute
// to one SalesByDay doc.
public class SalesByDayProjection : MultiStreamProjection<SalesByDay, string>
{
    public SalesByDayProjection()
    {
        Identity<CartSubmitted>(e => e.SubmittedAt.UtcDateTime.ToString("yyyy-MM-dd"));
    }

    public void Apply(CartSubmitted e, SalesByDay view)
    {
        view.CartCount++;
        view.TotalRevenue += e.TotalAmount;
    }
}
