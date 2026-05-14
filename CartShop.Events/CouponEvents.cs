namespace CartShop.Events;

// A coupon code being applied to a cart. Lives on the cart stream so cart
// history is complete; tagged with the coupon code so DCB can enforce
// single-use across every cart's stream.
public record CouponApplied(Guid CartId, CouponCode Code, DateTimeOffset At);
