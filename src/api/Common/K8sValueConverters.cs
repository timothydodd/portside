using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using k8s.Models;

namespace PortsideApi.Common;

// The AOT package's own converters for these two K8s scalar types are internal, so the
// source generator can't use them from our context. These mirror the upstream wire
// behavior: quantities serialize as their string form ("100m", "2Gi"); int-or-string
// values serialize as a number when numeric, otherwise as a string.

public sealed class AppResourceQuantityConverter : JsonConverter<ResourceQuantity>
{
    public override ResourceQuantity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.Number => new ResourceQuantity(reader.GetDouble().ToString(CultureInfo.InvariantCulture)),
            _ => new ResourceQuantity(reader.GetString()),
        };

    // 19.x dropped ResourceQuantity.Value; ToString() returns the canonical quantity string.
    public override void Write(Utf8JsonWriter writer, ResourceQuantity value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}

/// <summary>
/// Supplies converter-backed JsonTypeInfo for the two K8s scalar types the source
/// generator skips (their [JsonConverter] attributes point at internal converters).
/// Chain this after AppJsonContext in every TypeInfoResolverChain.
/// </summary>
public sealed class K8sScalarResolver : System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver
{
    public System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        if (type == typeof(ResourceQuantity))
            return System.Text.Json.Serialization.Metadata.JsonMetadataServices
                .CreateValueInfo<ResourceQuantity>(options, new AppResourceQuantityConverter());
        if (type == typeof(IntOrString))
            return System.Text.Json.Serialization.Metadata.JsonMetadataServices
                .CreateValueInfo<IntOrString>(options, new AppIntOrStringConverter());
        return null;
    }
}

public sealed class AppIntOrStringConverter : JsonConverter<IntOrString>
{
    public override IntOrString Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            // IntOrString has no public ctor in 19.x; implicit conversion from string.
            JsonTokenType.Number => (IntOrString)reader.GetInt64().ToString(CultureInfo.InvariantCulture),
            _ => (IntOrString)reader.GetString()!,
        };

    public override void Write(Utf8JsonWriter writer, IntOrString value, JsonSerializerOptions options)
    {
        if (long.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            writer.WriteNumberValue(n);
        else
            writer.WriteStringValue(value.Value);
    }
}
