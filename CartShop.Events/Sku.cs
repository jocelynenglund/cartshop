using System.Text.Json;
using System.Text.Json.Serialization;

namespace CartShop.Events;

// Strong-typed identifier for product SKUs. Registered as a Marten DCB tag type
// so any event carrying a Sku property is automatically tagged for cross-stream
// consistency queries (see Initialization.cs).
//
// JsonConverter flattens the JSON to a plain string so API DTOs stay clean.
[JsonConverter(typeof(SkuJsonConverter))]
public sealed record Sku(string Value)
{
    public override string ToString() => Value;
}

public sealed class SkuJsonConverter : JsonConverter<Sku>
{
    public override Sku Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? throw new JsonException("Sku cannot be null"));

    public override void Write(Utf8JsonWriter writer, Sku value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
