using CartShop.Core.Domain;
using CartShop.Events;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events.Dcb;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace CartShop.Core.Feature.Cart.Commands.ApplyCoupon;

public record ApplyCouponRequest(string Code);

public static class ApplyCouponEndpoint
{
    [WolverinePost("/api/carts/{cartId}/coupon")]
    public static async Task<IResult> Handle(
        Guid cartId,
        ApplyCouponRequest request,
        IDocumentSession session,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return Results.BadRequest(new { error = "Code is required" });

        var code = new CouponCode(request.Code.Trim().ToUpperInvariant());

        // DCB boundary across every cart for this coupon code. If any
        // CouponApplied with this code lands before SaveChangesAsync,
        // Marten throws DcbConcurrencyException.
        var couponQuery = EventTagQuery
            .For(code)
            .AndEventsOfType<CouponApplied>();

        IEventBoundary<CouponUsageView> boundary =
            await session.Events.FetchForWritingByTags<CouponUsageView>(couponQuery, ct);

        // Cart-side invariants: cart must exist and be open and not already
        // have a coupon. (Single-stream, not DCB.)
        var cart = await session.LoadAsync<CartAggregate>(cartId, ct);
        if (cart is null) return Results.NotFound();

        try
        {
            boundary.Aggregate?.EnsureNotUsed();
            var evt = cart.ApplyCoupon(code);

            var tagged = new Event<CouponApplied>(evt);
            tagged.AddTag(code);
            session.Events.Append(cartId, tagged);

            await session.SaveChangesAsync(ct);
            return Results.Ok(evt);
        }
        catch (CouponAlreadyUsed ex)
        {
            return Results.Conflict(new { error = ex.Message, code = ex.Code });
        }
        catch (CartAlreadySubmitted ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
        catch (CartRuleViolation ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (DcbConcurrencyException)
        {
            return Results.Conflict(new { error = "Coupon claimed concurrently; retry." });
        }
    }
}
