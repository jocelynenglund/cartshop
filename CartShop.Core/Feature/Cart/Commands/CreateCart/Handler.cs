using CartShop.Core.Domain;
using CartShop.Events;
using Marten;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace CartShop.Core.Feature.Cart.Commands.CreateCart;

public record CreateCartRequest(string CustomerName);

public record CreateCartResponse(Guid CartId);

public static class CreateCartEndpoint
{
    [WolverinePost("/api/carts")]
    public static async Task<IResult> Handle(
        CreateCartRequest request,
        IDocumentSession session,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerName))
            return Results.BadRequest(new { error = "CustomerName is required" });

        var cartId = Guid.NewGuid();
        var created = new CartCreated(cartId, request.CustomerName.Trim(), DateTimeOffset.UtcNow);

        session.Events.StartStream<CartAggregate>(cartId, created);
        await session.SaveChangesAsync(ct);

        return Results.Created($"/api/carts/{cartId}", new CreateCartResponse(cartId));
    }
}
