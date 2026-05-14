using System.Text.Json;
using System.Text.Json.Serialization;

namespace CartShop.Events;

// Strong-typed identifier for coupon codes. Registered as a Marten DCB tag type
// (Initialization.cs) so any event carrying a CouponCode property is automatically
// tagged for cross-stream consistency queries. Used by the ApplyCoupon slice to
// enforce single-use across every cart.
//
// JsonConverter flattens the JSON to a plain string so API DTOs stay clean.
[JsonConverter(typeof(CouponCodeJsonConverter))]
public sealed record CouponCode(string Value)
{
    public override string ToString() => Value;
}

public sealed class CouponCodeJsonConverter : JsonConverter<CouponCode>
{
    public override CouponCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? throw new JsonException("CouponCode cannot be null"));

    public override void Write(Utf8JsonWriter writer, CouponCode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
