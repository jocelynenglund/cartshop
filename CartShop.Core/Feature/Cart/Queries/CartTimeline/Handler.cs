using Marten;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace CartShop.Core.Feature.Cart.Queries.CartTimeline;

public record CartTimelineEntry(long Sequence, string EventType, DateTimeOffset Timestamp, object Data);

public static class CartTimelineEndpoint
{
    [WolverineGet("/api/carts/{cartId}/timeline")]
    public static async Task<IResult> Handle(
        Guid cartId,
        IQuerySession session,
        CancellationToken ct)
    {
        // *Live* projection: replay the stream on demand, project each IEvent
        // into a UI-friendly shape, throw away the result after the response.
        // Nothing about this view is stored. Marten reads straight from the
        // event log; cost is one SELECT per request, cheap for a few-dozen-
        // event cart stream.
        var events = await session.Events.FetchStreamAsync(cartId, token: ct);
        if (events.Count == 0) return Results.NotFound();

        var timeline = events
            .Select(e => new CartTimelineEntry(e.Sequence, e.EventType.Name, e.Timestamp, e.Data))
            .ToList();

        return Results.Ok(timeline);
    }
}
