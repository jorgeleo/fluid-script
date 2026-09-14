using System.Globalization;

namespace FluidScript.Runtime;

public enum FluidValueKind
{
    Null,
    Bool,
    Int,
    Decimal,
    String,
    Array,
    DateTime,
    Guid,
    Byte,
    Function,
    Object,
    Cell
}

public readonly record struct FluidValue(FluidValueKind Kind, object? Raw)
{
    public static FluidValue Null => new(FluidValueKind.Null, null);
    public static FluidValue From(bool value) => new(FluidValueKind.Bool, value);
    public static FluidValue From(long value) => new(FluidValueKind.Int, value);
    public static FluidValue From(decimal value) => new(FluidValueKind.Decimal, value);
    public static FluidValue From(string value) => new(FluidValueKind.String, value);
    public static FluidValue FromArray(IReadOnlyList<FluidValue> value) => new(FluidValueKind.Array, value);
    public static FluidValue From(DateTimeOffset value) => new(FluidValueKind.DateTime, value);
    public static FluidValue From(Guid value) => new(FluidValueKind.Guid, value);
    public static FluidValue From(byte value) => new(FluidValueKind.Byte, value);
    public static FluidValue FromFunction(int functionId) => new(FluidValueKind.Function, new FunctionHandle(functionId));
    public static FluidValue FromFunction(int functionId, IReadOnlyList<FluidCell> captures) => new(FluidValueKind.Function, new FunctionHandle(functionId, captures));
    public static FluidValue FromObject(FluidObject value) => new(FluidValueKind.Object, value);
    public static FluidValue FromCell(FluidCell value) => new(FluidValueKind.Cell, value);

    public bool AsBool() => Kind == FluidValueKind.Bool
        ? (bool)Raw!
        : throw new InvalidOperationException("Expected a bool value.");

    public long AsInt() => Kind == FluidValueKind.Int
        ? (long)Raw!
        : throw new InvalidOperationException("Expected an integer value.");

    public decimal AsDecimal() => Kind switch
    {
        FluidValueKind.Int => AsInt(),
        FluidValueKind.Decimal => (decimal)Raw!,
        _ => throw new InvalidOperationException("Expected a numeric value.")
    };

    public string AsString() => Kind == FluidValueKind.String
        ? (string)Raw!
        : throw new InvalidOperationException("Expected a string value.");

    public IReadOnlyList<FluidValue> AsArray() => Kind == FluidValueKind.Array
        ? (IReadOnlyList<FluidValue>)Raw!
        : throw new InvalidOperationException("Expected an array value.");

    public FluidObject AsObject() => Kind == FluidValueKind.Object
        ? (FluidObject)Raw!
        : throw new InvalidOperationException("Expected an object value.");

    public FluidCell AsCell() => Kind == FluidValueKind.Cell
        ? (FluidCell)Raw!
        : throw new InvalidOperationException("Expected a captured cell.");

    public override string ToString() => Kind switch
    {
        FluidValueKind.Null => "null",
        FluidValueKind.Bool => AsBool() ? "true" : "false",
        FluidValueKind.Int => AsInt().ToString(CultureInfo.InvariantCulture),
        FluidValueKind.Decimal => ((decimal)Raw!).ToString(CultureInfo.InvariantCulture),
        FluidValueKind.String => AsString(),
        FluidValueKind.Array => "[" + string.Join(", ", AsArray().Select(value => value.ToString())) + "]",
        FluidValueKind.DateTime => ((DateTimeOffset)Raw!).ToString("O", CultureInfo.InvariantCulture),
        FluidValueKind.Guid => ((Guid)Raw!).ToString("D"),
        FluidValueKind.Byte => "0x" + ((byte)Raw!).ToString("X2", CultureInfo.InvariantCulture),
        FluidValueKind.Function => $"<function {((FunctionHandle)Raw!).FunctionId}>",
        FluidValueKind.Object => AsObject().ToString(),
        FluidValueKind.Cell => AsCell().Value.ToString(),
        _ => Raw?.ToString() ?? "null"
    };
}

public readonly record struct FunctionHandle(int FunctionId, IReadOnlyList<FluidCell>? Captures = null);

public sealed class FluidCell(FluidValue value)
{
    public FluidValue Value { get; set; } = value;
}

public sealed class FluidObject
{
    public FluidObject(string typeName, IReadOnlyDictionary<string, FluidValue> fields, IReadOnlySet<string>? constantFields = null)
    {
        TypeName = typeName;
        Fields = new Dictionary<string, FluidValue>(fields, StringComparer.Ordinal);
        ConstantFields = constantFields ?? new HashSet<string>(StringComparer.Ordinal);
    }

    public string TypeName { get; }
    public Dictionary<string, FluidValue> Fields { get; }
    public IReadOnlySet<string> ConstantFields { get; }

    public override string ToString() => $"{TypeName}{{{string.Join(", ", Fields.Select(field => $"{field.Key}={field.Value}"))}}}";
}
