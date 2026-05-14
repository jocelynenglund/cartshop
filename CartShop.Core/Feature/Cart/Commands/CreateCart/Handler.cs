using CartShop.Core.Domain;
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
        var cartId = Guid.NewGuid();

        try
        {
            var created = CartAggregate.Create(cartId, request.CustomerName);

            // Inline-projection-backed uniqueness check. Because
            // OpenCartByCustomerProjection runs in the same transaction as
            // its events, the previous CreateCart's commit is visible here.
            var normalized = OpenCartByCustomer.Normalize(created.CustomerName);
            var alreadyOpen = await session.Query<OpenCartByCustomer>()
                .AnyAsync(c => c.CustomerNameNormalized == normalized, ct);
            if (alreadyOpen)
                throw new CustomerAlreadyHasOpenCart(created.CustomerName);

            session.Events.StartStream<CartAggregate>(cartId, created);
            await session.SaveChangesAsync(ct);
            return Results.Created($"/api/carts/{cartId}", new CreateCartResponse(cartId));
        }
        catch (CustomerAlreadyHasOpenCart ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
        catch (CartRuleViolation ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
