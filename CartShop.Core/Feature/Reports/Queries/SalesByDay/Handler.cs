using CartShop.Core.Domain;
using Marten;
using Wolverine.Http;

namespace CartShop.Core.Feature.Reports.Queries.SalesByDay;

public record SalesByDayRow(string Date, int CartCount, decimal TotalRevenue);

public static class SalesByDayEndpoint
{
    [WolverineGet("/api/reports/sales-by-day")]
    public static async Task<IReadOnlyList<SalesByDayRow>> Handle(
        IQuerySession session,
        CancellationToken ct)
    {
        // Pure document query — the async daemon keeps this collection fresh.
        // If the daemon is paused or behind, we serve stale data without error;
        // this is by design for a reporting view.
        var rows = await session.Query<Domain.SalesByDay>()
            .OrderByDescending(s => s.Id)
            .Take(30)
            .ToListAsync(ct);

        return rows
            .Select(r => new SalesByDayRow(r.Id, r.CartCount, r.TotalRevenue))
            .ToList();
    }
}
