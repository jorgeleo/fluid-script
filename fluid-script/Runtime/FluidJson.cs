using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace FluidScript.Runtime;

/// <summary>Converts JSON-native FluidScript values to and from standard JSON.</summary>
public static class FluidJson
{
    public static string Serialize(FluidValue value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteValue(writer, value, new HashSet<object>(ReferenceComparer.Instance));
            writer.Flush();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static FluidValue Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        return ReadValue(document.RootElement);
    }

    private static void WriteValue(Utf8JsonWriter writer, FluidValue value, ISet<object> ancestors)
    {
        switch (value.Kind)
        {
            case FluidValueKind.Null:
                writer.WriteNullValue();
                return;
            case FluidValueKind.Bool:
                writer.WriteBooleanValue(value.AsBool());
                return;
            case FluidValueKind.Int:
                writer.WriteNumberValue(value.AsInt());
                return;
            case FluidValueKind.Decimal:
                writer.WriteNumberValue(value.AsDecimal());
                return;
            case FluidValueKind.String:
                writer.WriteStringValue(value.AsString());
                return;
            case FluidValueKind.Array:
                WriteArray(writer, value.AsArray(), value.Raw!, ancestors);
                return;
            case FluidValueKind.Dictionary:
                WriteDictionary(writer, value.AsDictionary(), ancestors);
                return;
            case FluidValueKind.Object:
                WriteObject(writer, value.AsObject(), ancestors);
                return;
            default:
                throw new InvalidOperationException($"FluidScript value kind {value.Kind} is not JSON-native.");
        }
    }

    private static void WriteArray(Utf8JsonWriter writer, IReadOnlyList<FluidValue> values, object identity, ISet<object> ancestors)
    {
        Enter(ancestors, identity);
        try
        {
            writer.WriteStartArray();
            foreach (var value in values)
                WriteValue(writer, value, ancestors);
            writer.WriteEndArray();
        }
        finally
        {
            ancestors.Remove(identity);
        }
    }

    private static void WriteDictionary(Utf8JsonWriter writer, FluidDictionary dictionary, ISet<object> ancestors)
    {
        Enter(ancestors, dictionary);
        try
        {
            writer.WriteStartObject();
            foreach (var entry in dictionary.Entries.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(entry.Key);
                WriteValue(writer, entry.Value, ancestors);
            }
            writer.WriteEndObject();
        }
        finally
        {
            ancestors.Remove(dictionary);
        }
    }

    private static void WriteObject(Utf8JsonWriter writer, FluidObject value, ISet<object> ancestors)
    {
        Enter(ancestors, value);
        try
        {
            writer.WriteStartObject();
            foreach (var field in value.Fields.OrderBy(field => field.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(field.Key);
                WriteValue(writer, field.Value, ancestors);
            }
            writer.WriteEndObject();
        }
        finally
        {
            ancestors.Remove(value);
        }
    }

    private static void Enter(ISet<object> ancestors, object identity)
    {
        if (!ancestors.Add(identity))
            throw new InvalidOperationException("Cyclic arrays, dictionaries, and objects cannot be serialized as JSON.");
    }

    private static FluidValue ReadValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null => FluidValue.Null,
        JsonValueKind.True => FluidValue.From(true),
        JsonValueKind.False => FluidValue.From(false),
        JsonValueKind.String => FluidValue.From(element.GetString() ?? string.Empty),
        JsonValueKind.Number => ReadNumber(element),
        JsonValueKind.Array => FluidValue.FromArray(element.EnumerateArray().Select(ReadValue).ToList()),
        JsonValueKind.Object => ReadDictionary(element),
        _ => throw new JsonException($"JSON token {element.ValueKind} is not supported.")
    };

    private static FluidValue ReadNumber(JsonElement element)
    {
        if (element.TryGetInt64(out var integer))
            return FluidValue.From(integer);
        if (element.TryGetDecimal(out var decimalValue))
            return FluidValue.From(decimalValue);
        throw new JsonException("JSON number is outside the supported integer and decimal ranges.");
    }

    private static FluidValue ReadDictionary(JsonElement element)
    {
        var entries = new Dictionary<string, FluidValue>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            entries[property.Name] = ReadValue(property.Value);
        return FluidValue.FromDictionary(new FluidDictionary(entries));
    }

    public static FluidValue DeserializeObject(string json, PCodeType type)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(type);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException($"JSON for '{type.Name}' must be an object.");

        var fieldNames = new HashSet<string>(type.FieldNames, StringComparer.Ordinal);
        var fields = new Dictionary<string, FluidValue>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!fieldNames.Contains(property.Name))
                throw new JsonException($"JSON property '{property.Name}' is not declared by '{type.Name}'.");
            fields[property.Name] = ReadValue(property.Value);
        }
        foreach (var fieldName in type.FieldNames)
        {
            if (!fields.ContainsKey(fieldName))
                throw new JsonException($"JSON for '{type.Name}' is missing declared property '{fieldName}'.");
        }
        return FluidValue.FromObject(new FluidObject(type.Name, fields, type.ConstantFields));
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static ReferenceComparer Instance { get; } = new();

        public new bool Equals(object? left, object? right) => ReferenceEquals(left, right);

        public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
    }
}
