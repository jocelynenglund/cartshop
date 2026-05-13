using CartShop.Core.Domain;
using Marten;
using Wolverine.Http;

namespace CartShop.Core.Feature.Cart.Queries.ListSubmittedCarts;

public record SubmittedCartSummary(Guid Id, string CustomerName, decimal Total, DateTimeOffset? SubmittedAt, int LineCount);

public static class ListSubmittedCartsEndpoint
{
    [WolverineGet("/api/carts/submitted")]
    public static async Task<IReadOnlyList<SubmittedCartSummary>> Handle(
        IQuerySession session,
        CancellationToken ct)
    {
        var carts = await session.Query<CartAggregate>()
            .Where(c => c.Status == CartStatus.Submitted)
            .ToListAsync(ct);

        return carts
            .OrderByDescending(c => c.SubmittedAt)
            .Select(c => new SubmittedCartSummary(c.Id, c.CustomerName, c.Total, c.SubmittedAt, c.Lines.Count))
            .ToList();
    }
}
