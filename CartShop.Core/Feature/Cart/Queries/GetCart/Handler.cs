using CartShop.Core.Domain;
using Marten;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace CartShop.Core.Feature.Cart.Queries.GetCart;

public static class GetCartEndpoint
{
    [WolverineGet("/api/carts/{cartId}")]
    public static async Task<IResult> Handle(
        Guid cartId,
        IQuerySession session,
        CancellationToken ct)
    {
        var cart = await session.LoadAsync<CartAggregate>(cartId, ct);
        return cart is null ? Results.NotFound() : Results.Ok(cart);
    }
}
